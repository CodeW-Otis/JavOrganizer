using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Scheduled task that maintains the JavOrganizer collections from the
/// scraped library:
/// <list type="bullet">
/// <item><b>Female Actresses</b>, <b>Male Actors</b> and <b>Studios</b> browse
/// cards whose members are one collection per performer or studio,</item>
/// <item>each <b>performer collection</b> holds that performer's own videos
/// and carries their photo (or a cover from their own titles) as its
/// poster,</item>
/// <item>"Most Viewed" / "Most Liked" and "All Videos" / "Newest Releases"
/// rankings aggregated across every user,</item>
/// <item>optional collections per studio, genre and release year, each group
/// switched on independently in the plugin settings.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The nesting is a real Jellyfin hierarchy, not a flat mix: a card's
/// members are box sets, and each of those box sets holds movies. Opening
/// <i>Female Actresses</i> therefore shows performer cards, and opening one
/// performer shows that performer's videos — the structure the library
/// browser is built around.
/// </para>
/// <para>
/// Getting that right on Jellyfin 10.9+ means writing the membership as
/// linked-child entries on the container itself.
/// <c>ICollectionManager.AddToCollectionAsync</c> only ever appends movies
/// to a box set's link list, so a card built that way ends up holding the
/// performer's <i>videos</i> instead of the performer <i>collections</i> —
/// which is exactly what made the gender cards show hundreds of loose
/// movies. Box sets are linked directly here, and any set that also needs a
/// real folder on disk is materialised first.
/// </para>
/// <para>
/// Runs daily after the cache cleanup so the collections reflect fresh
/// scrapes.
/// </para>
/// </remarks>
public sealed class JavCollectionsTask : IScheduledTask, IConfigurableScheduledTask
{
    private const string MostViewedName = "Most Viewed (JavOrganizer)";
    private const string MostLikedName = "Most Liked (JavOrganizer)";
    private const string AllVideosName = "All Videos (JavOrganizer)";
    private const string NewestReleasesName = "Newest Releases (JavOrganizer)";
    private const string FemaleCardName = "Female Actresses (JavOrganizer)";
    private const string MaleCardName = "Male Actors (JavOrganizer)";
    private const string StudiosCardName = "Studios (JavOrganizer)";

    /// <summary>
    /// Name prefixes that mark a collection as belonging to one performer.
    /// </summary>
    private static readonly string[] PersonPrefixes = ["Actress: ", "Actor: "];

    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<JavCollectionsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavCollectionsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager used to query items and people.</param>
    /// <param name="collectionManager">Collection manager used to create and update collections.</param>
    /// <param name="userDataManager">User data manager providing play and like state.</param>
    /// <param name="userManager">User manager enumerating users to aggregate over.</param>
    /// <param name="logger">Logger scoped to this task.</param>
    public JavCollectionsTask(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILogger<JavCollectionsTask> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Update JavOrganizer Collections";

    /// <inheritdoc />
    public string Key => "JavOrganizerCollectionsUpdate";

    /// <inheritdoc />
    public string Description => "Rebuilds the selected JavOrganizer collections: the All Videos / Newest Releases cards, the Female Actresses, Male Actors and Studios browse cards (one collection per performer or studio inside, with a photo as the poster), and per studio, genre and year collections. Each group has its own switch in the plugin settings; a group that is switched off has its collections removed.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <summary>
    /// One performer's credit: the videos they appear in, their gender, the
    /// role label shown on their collection, and the collection id once
    /// it exists.
    /// </summary>
    private sealed class PerformerEntry
    {
        /// <summary>Gets the ids of the videos this performer appears in.</summary>
        public List<Guid> Items { get; } = [];

        /// <summary>Gets or sets the performer's gender ("female" or "male").</summary>
        public string Gender { get; set; } = string.Empty;

        /// <summary>Gets or sets the role label ("Actress", "Male actor", "Director").</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>Gets or sets the id of the performer's collection, once synced.</summary>
        public Guid CollectionId { get; set; }

        /// <summary>Gets or sets the total play count across the performer's titles.</summary>
        public int TotalViews { get; set; }

        /// <summary>Gets or sets the poster source used for the performer's card and collection.</summary>
        public ImageSource? Poster { get; set; }
    }

    /// <summary>
    /// Where a card or collection poster comes from: the performer's own
    /// photo when a site published one, otherwise a cover from one of their
    /// titles — so no performer card is ever left blank.
    /// </summary>
    private sealed class ImageSource
    {
        /// <summary>Gets or sets the remote image URL.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Gets or sets the row id used to make the cached file name stable per source.</summary>
        public string CacheKey { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether this is the performer's own photo.</summary>
        public bool IsPortrait { get; set; }
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.EffectiveConfiguration;
        if (!config.BuildCollections)
        {
            _logger.LogInformation("JavOrganizer collections are disabled in the plugin settings");
            return;
        }

        var items = ListScrapedVideos();
        if (items.Count == 0)
        {
            _logger.LogInformation("No JavOrganizer-scraped videos found; nothing to collect");
            return;
        }

        // The item map backs release-date ordering and per-video lookups.
        var byId = items.ToDictionary(i => i.Id);

        // Gender knowledge and performer photos come from the plugin's own
        // scrape cache: each cached record holds separated female/male cast
        // lists and the portraits the site embedded, which is the only
        // reliable source (Jellyfin people carry no gender and often no
        // image).
        var indexes = JavCache.LoadIndexes(_logger);
        var genderByPerson = indexes.Gender;
        var aliasByPerson = indexes.Alias;
        var photoByPerson = LoadPersonPhotos(items, indexes.Photos);

        // A performer written two ways ("Yua Mikami" / "Yua Mikami（三上悠亜）")
        // is one person: the sites' star ids say so. Carry the photo across to
        // the name that will be displayed, or the merged card loses its face.
        foreach (var (rawName, canonicalName) in aliasByPerson)
        {
            if (!photoByPerson.ContainsKey(canonicalName) && photoByPerson.TryGetValue(rawName, out var photo))
            {
                photoByPerson[canonicalName] = photo;
            }
        }

        var byPerson = CollectPerformers(items, genderByPerson, aliasByPerson);
        if (aliasByPerson.Count > 0)
        {
            _logger.LogInformation(
                "JavOrganizer collections: merged {Count} performer name spellings that share a star id",
                aliasByPerson.Count);
        }
        _logger.LogInformation(
            "JavOrganizer collections: {Videos} videos, {Performers} credited people, {Photos} with a cached photo",
            items.Count,
            byPerson.Count,
            photoByPerson.Count);
        progress.Report(15);

        // ---- Per-performer collections (movies inside) ----
        var peopleBuilt = 0;
        foreach (var (person, entry) in byPerson)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Items.Count < Math.Max(1, config.MinVideosPerCollection))
            {
                continue;
            }

            if (!IsPerformerGroupEnabled(entry.Gender, config))
            {
                // The person's wall is switched off, so no per-person
                // collection is built for them this run.
                continue;
            }

            var ordered = OrderByRelease(byId, entry.Items);
            var collectionName = PrefixFor(entry.Gender) + person;
            var overview = $"{entry.Role} — appears in {entry.Items.Count} videos in this library.";

            var collection = await SyncMovieCollectionAsync(collectionName, ordered, overview, cancellationToken)
                .ConfigureAwait(false);
            if (collection is null)
            {
                continue;
            }

            entry.CollectionId = collection.Id;
            peopleBuilt++;

            // The performer's poster: their own photo when a site published
            // one, otherwise a cover from one of their own titles. Applied
            // to both the person item (the face shown on cast rows) and the
            // collection card.
            entry.Poster = ResolvePoster(person, entry.Items, byId, photoByPerson);
            if (entry.Poster is not null)
            {
                var stored = await StoreImageAsync(collection, person, entry.Poster, cancellationToken).ConfigureAwait(false);
                TrySetPersonItemImage(person, entry.Poster, stored);
            }

            progress.Report(15 + (25.0 * peopleBuilt / Math.Max(1, byPerson.Count)));
        }

        // Retire performer collections left over from earlier runs that are
        // no longer warranted (a re-scraped library can drop a person below
        // the minimum, or split one name into two spellings).
        var retiredPeople = RetireStalePersonCollections(byPerson, config.MinVideosPerCollection);

        // ---- Library-wide play / like stats, aggregated over all users ----
        progress.Report(45);
        var (playCounts, likeScores) = await AggregateUserDataAsync(items, cancellationToken).ConfigureAwait(false);

        foreach (var (person, entry) in byPerson)
        {
            var views = 0;
            foreach (var id in entry.Items)
            {
                playCounts.TryGetValue(id, out var plays);
                views += plays;
            }

            entry.TotalViews = views;
        }

        // ---- Most Viewed / Most Liked ----
        progress.Report(60);
        var mostViewed = playCounts
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(100)
            .Select(kv => kv.Key)
            .ToList();
        if (mostViewed.Count > 0)
        {
            await SyncMovieCollectionAsync(MostViewedName, mostViewed, "Top 100 most played videos across all users.", cancellationToken).ConfigureAwait(false);
        }

        progress.Report(65);
        var mostLiked = likeScores
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(100)
            .Select(kv => kv.Key)
            .ToList();
        if (mostLiked.Count > 0)
        {
            await SyncMovieCollectionAsync(MostLikedName, mostLiked, "Top 100 videos by likes and favorites across all users.", cancellationToken).ConfigureAwait(false);
        }

        // ---- Gender browse cards (performer collections inside) ----
        // These are the two browse-everything cards. Their members are the
        // per-performer collections built above — so a card shows performer
        // cards, and a performer card shows that performer's videos.
        // Performers are ordered by total views, most watched first.
        progress.Report(70);
        var femaleMembers = config.BuildActressCollections
            ? GenderMembers(byPerson, "female", config.MinVideosPerCollection)
            : [];
        var maleMembers = config.BuildActorCollections
            ? GenderMembers(byPerson, "male", config.MinVideosPerCollection)
            : [];

        // An empty member list also retires the card, so switching a wall off
        // removes it rather than leaving an empty shell behind.
        await SyncNestedCardAsync(
            FemaleCardName,
            ToCardMembers(femaleMembers),
            "Every actress in this library, each with her own collection inside this card — most watched first. Open one to see her videos.",
            FindTopPoster(femaleMembers),
            cancellationToken).ConfigureAwait(false);

        await SyncNestedCardAsync(
            MaleCardName,
            ToCardMembers(maleMembers),
            "Every male actor in this library, each with his own collection inside this card — most watched first. Open one to see his videos.",
            FindTopPoster(maleMembers),
            cancellationToken).ConfigureAwait(false);

        // ---- All Videos / Newest Releases ----
        progress.Report(78);
        var allOrdered = OrderByRelease(byId, [.. byId.Keys]);
        if (config.BuildAllVideosCollection)
        {
            await SyncMovieCollectionAsync(AllVideosName, allOrdered, $"Every scraped video in this library ({allOrdered.Count} titles), newest first.", cancellationToken).ConfigureAwait(false);

            progress.Report(82);
            await SyncMovieCollectionAsync(NewestReleasesName, allOrdered.Take(100).ToList(), "The 100 most recently released videos in this library.", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            DeleteCollectionByName(AllVideosName);
            DeleteCollectionByName(NewestReleasesName);
        }

        // ---- Optional studio / genre / year collections ----
        var studioCollections = new List<(string Name, List<Guid> Ids, Guid CollectionId)>();
        var genreCollections = new List<(string Name, List<Guid> Ids, Guid CollectionId)>();
        var yearCollections = new List<(string Name, List<Guid> Ids, Guid CollectionId)>();

        if (config.BuildStudioCollections)
        {
            progress.Report(86);
            studioCollections = await SyncGroupedCollectionsAsync(
                items,
                item => item.Studios,
                "Studio: {0}",
                (name, ids) => $"All {ids.Count} videos from studio {name} in this library.",
                config.MinVideosPerCollection,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            RetireGroupedCollections("Studio: ");
        }

        if (config.BuildGenreCollections)
        {
            progress.Report(90);
            genreCollections = await SyncGroupedCollectionsAsync(
                items,
                item => item.Genres,
                "Genre: {0}",
                (name, ids) => $"{ids.Count} {name} videos in this library.",
                config.MinVideosPerCollection,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            RetireGroupedCollections("Genre: ");
        }

        if (config.BuildYearCollections)
        {
            progress.Report(94);
            yearCollections = await SyncGroupedCollectionsAsync(
                items,
                item => item.ProductionYear is > 1900 ? [item.ProductionYear.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)] : [],
                "Year: {0}",
                (name, ids) => $"{ids.Count} videos released in {name}.",
                config.MinVideosPerCollection,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            RetireGroupedCollections("Year: ");
        }

        // ---- Studio card (studio collections inside, like the performer walls) ----
        var studioMembers = studioCollections
            .Select(s => (s.Name, s.CollectionId))
            .ToList();
        ImageSource? studioPoster = null;
        if (studioCollections.Count > 0)
        {
            var biggest = studioCollections.OrderByDescending(s => s.Ids.Count).First();
            studioPoster = ResolvePoster(biggest.Name, biggest.Ids, byId, photoByPerson);
        }

        await SyncNestedCardAsync(
            StudiosCardName,
            studioMembers,
            "Every studio in this library, each with its own collection inside this card. Open one to browse that studio's videos.",
            studioPoster,
            cancellationToken).ConfigureAwait(false);

        var groupsBuilt = studioCollections.Count + genreCollections.Count + yearCollections.Count;

        _logger.LogInformation(
            "JavOrganizer collections updated: {People} performer collections ({Female} female / {Male} male), {Groups} studio/genre/year collections, {Viewed} most-viewed, {Liked} most-liked, {Total} all-videos, {Retired} stale performer collections retired",
            peopleBuilt,
            femaleMembers.Count,
            maleMembers.Count,
            groupsBuilt,
            mostViewed.Count,
            mostLiked.Count,
            config.BuildAllVideosCollection ? allOrdered.Count : 0,
            retiredPeople);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // After the cache cleanup, so people from fresh scrapes are in.
        yield return JavApiCompat.DailyTrigger(TimeSpan.FromHours(4).Add(TimeSpan.FromMinutes(30)));
    }

    // -----------------------------------------------------------------
    // Performer discovery
    // -----------------------------------------------------------------

    /// <summary>
    /// Groups every credited performer by the videos they appear in.
    /// Directors are grouped too, but a credit whose gender cannot be
    /// resolved belongs to no wall and therefore gets no collection — it
    /// still shows on the movie's cast row, which is where Jellyfin reads it.
    /// </summary>
    /// <param name="items">All scraped videos.</param>
    /// <param name="genderByPerson">Performer → gender index from the scrape cache.</param>
    /// <param name="aliasByPerson">Performer spelling → the display name kept for it.</param>
    /// <returns>Performer name → credit entry.</returns>
    private Dictionary<string, PerformerEntry> CollectPerformers(
        List<Movie> items,
        Dictionary<string, string> genderByPerson,
        Dictionary<string, string> aliasByPerson)
    {
        var byPerson = new Dictionary<string, PerformerEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            foreach (var person in JavApiCompat.GetPeople(_libraryManager, item))
            {
                if (string.IsNullOrWhiteSpace(person.Name))
                {
                    continue;
                }

                // One performer, one key: the sites' shared star ids decide
                // which spellings mean the same person, so "Yua Mikami" and
                // "Yua Mikami（三上悠亜）" end up on a single card.
                var name = aliasByPerson.TryGetValue(person.Name, out var canonical)
                    ? canonical
                    : person.Name;

                if (!byPerson.TryGetValue(name, out var entry))
                {
                    var gender = genderByPerson.TryGetValue(person.Name, out var known)
                        ? known
                        : genderByPerson.TryGetValue(name, out known)
                            ? known
                            : GuessGender(person.Name, item);
                    entry = new PerformerEntry
                    {
                        Gender = gender,
                        Role = DescribeRole(person, gender)
                    };
                    byPerson[name] = entry;
                }

                if (!entry.Items.Contains(item.Id))
                {
                    entry.Items.Add(item.Id);
                }
            }
        }

        return byPerson;
    }

    /// <summary>
    /// Guesses a person's gender from an item's cached scrape record when
    /// the global index has no entry yet. Directors stay ungendered.
    /// </summary>
    /// <param name="personName">The person to look up.</param>
    /// <param name="item">An item the person appears in.</param>
    /// <returns>"female", "male" or an empty string.</returns>
    private static string GuessGender(string personName, Movie item)
    {
        var record = JavCache.TryReadForItem(item.Path, item.Name, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        if (record is null)
        {
            return string.Empty;
        }

        if (record.Actresses.Any(a => string.Equals(a, personName, StringComparison.OrdinalIgnoreCase)))
        {
            return "female";
        }

        if (record.MaleActors.Any(a => string.Equals(a, personName, StringComparison.OrdinalIgnoreCase)))
        {
            return "male";
        }

        return string.Empty;
    }

    /// <summary>
    /// Describes a person's credited role for the collection overview,
    /// gender-aware where the gender is known.
    /// </summary>
    /// <param name="person">The credited person.</param>
    /// <param name="gender">Known gender: "female", "male" or empty.</param>
    /// <returns>A short role label, e.g. "Actress" or "Male actor".</returns>
    private static string DescribeRole(PersonInfo person, string gender)
    {
        var isDirector = JavApiCompat.IsDirector(person);
        if (isDirector)
        {
            return "Director";
        }

        return gender switch
        {
            "female" => "Actress",
            "male" => "Male actor",
            _ => "Actress/Actor"
        };
    }

    /// <summary>
    /// Maps a gender to the collection-name prefix used for that person's
    /// own collection ("Actress: …" / "Actor: …").
    /// </summary>
    /// <param name="gender">"female", "male" or empty.</param>
    /// <returns>The name prefix.</returns>
    private static string PrefixFor(string gender) => gender switch
    {
        "female" => "Actress: ",
        "male" => "Actor: ",
        _ => string.Empty
    };

    /// <summary>
    /// Orders item ids by their release date, newest first, so every
    /// collection is browsable chronologically. Items without a date sink
    /// to the end in name order.
    /// </summary>
    /// <param name="byId">Item id → movie map.</param>
    /// <param name="ids">The ids to order.</param>
    /// <returns>Release-date-ordered ids.</returns>
    private static List<Guid> OrderByRelease(Dictionary<Guid, Movie> byId, IEnumerable<Guid> ids)
    {
        return ids
            .Where(byId.ContainsKey)
            .OrderByDescending(id => byId[id].PremiereDate ?? DateTime.MinValue)
            .ThenBy(id => byId[id].SortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Picks the performers that belong on one gender card, most watched
    /// first, each with the collection id built earlier in this run.
    /// </summary>
    /// <param name="byPerson">Performer entries.</param>
    /// <param name="gender">"female" or "male".</param>
    /// <param name="minVideos">Minimum videos a performer needs for a collection.</param>
    /// <returns>Ordered (performer, entry) pairs that have a collection.</returns>
    private static List<(string Name, PerformerEntry Entry)> GenderMembers(
        Dictionary<string, PerformerEntry> byPerson,
        string gender,
        int minVideos)
    {
        var minimum = Math.Max(1, minVideos);
        return byPerson
            .Where(kv => kv.Value.Gender == gender
                && kv.Value.CollectionId != Guid.Empty
                && kv.Value.Items.Count >= minimum)
            .OrderByDescending(kv => kv.Value.TotalViews)
            .ThenByDescending(kv => kv.Value.Items.Count)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Picks the poster for a gender card: the top performer's photo when
    /// one exists, otherwise the top performer's video-cover fallback.
    /// </summary>
    /// <param name="members">Ordered card members.</param>
    /// <returns>An image source, or <c>null</c> when nothing is available.</returns>
    private static ImageSource? FindTopPoster(List<(string Name, PerformerEntry Entry)> members)
        => members.Select(m => m.Entry.Poster).FirstOrDefault(p => p is not null);

    // -----------------------------------------------------------------
    // Poster resolution
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds the performer-name → photo-URL map, preferring fresh cached
    /// records and falling back to stale ones so a photo cached months ago
    /// still puts a face on the card.
    /// </summary>
    /// <param name="items">All scraped videos.</param>
    /// <param name="photos">Photo index from the cache pass this run already made.</param>
    /// <returns>Performer name → portrait URL.</returns>
    private Dictionary<string, string> LoadPersonPhotos(List<Movie> items, Dictionary<string, string> photos)
    {
        if (photos.Count > 0)
        {
            return photos;
        }

        // Nothing fresh in the cache: every cached record has aged out of
        // its TTL, but the URLs are still perfectly good image sources.
        _logger.LogInformation("JavOrganizer: no fresh cached performer photos; reading expired cache records for portraits");

        foreach (var item in items)
        {
            var stale = JavCache.TryReadStaleForItem(item.Path, item.Name, _logger);
            if (stale is null)
            {
                continue;
            }

            foreach (var (name, url) in stale.PersonImageUrls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    photos.TryAdd(name, url);
                }
            }
        }

        _logger.LogInformation(
            "JavOrganizer: recovered {Count} performer portraits from expired cache records",
            photos.Count);
        return photos;
    }

    /// <summary>
    /// Resolves the image that should appear on a performer's collection
    /// card and person item: their own photo when a site published one,
    /// otherwise a still from the cover of one of their own titles — so a
    /// performer with videos never shows an empty card.
    /// </summary>
    /// <param name="person">Performer name.</param>
    /// <param name="itemIds">The performer's video ids.</param>
    /// <param name="byId">Item id → movie map.</param>
    /// <param name="photoByPerson">Performer name → portrait URL.</param>
    /// <returns>The poster source, or <c>null</c> when neither exists.</returns>
    private static ImageSource? ResolvePoster(
        string person,
        List<Guid> itemIds,
        Dictionary<Guid, Movie> byId,
        Dictionary<string, string> photoByPerson)
    {
        if (photoByPerson.TryGetValue(person, out var portrait) && !string.IsNullOrWhiteSpace(portrait))
        {
            return new ImageSource { Url = portrait, CacheKey = "portrait", IsPortrait = true };
        }

        // Name drift between sites ("Yuzuru Yuki" / "Yuzuru Yuuki") would
        // otherwise cost a performer their photo.
        var wanted = SiteScraper.LabelFragment(person);
        if (wanted.Length > 0)
        {
            foreach (var (name, url) in photoByPerson)
            {
                if (string.Equals(SiteScraper.LabelFragment(name), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return new ImageSource { Url = url, CacheKey = "portrait", IsPortrait = true };
                }
            }
        }

        // Fallback: the cover of the performer's most recent scraped title.
        foreach (var id in itemIds)
        {
            if (byId.TryGetValue(id, out var item)
                && item.ProviderIds.TryGetValue(JavMetadataProvider.ProviderIdKey, out var videoId))
            {
                var record = JavCache.FindByVideoId(videoId, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
                if (record?.CoverUrl is { Length: > 0 } cover && JavHttp.IsUsableImageUrl(cover))
                {
                    return new ImageSource { Url = cover, CacheKey = id.ToString("N"), IsPortrait = false };
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads a poster through the shared image pipeline and attaches it
    /// to the collection. The image is stored on disk (so Jellyfin can also
    /// generate the landscape/backdrop crops it needs) and referenced by
    /// path; if the download fails, the remote URL is attached instead so
    /// the card still renders.
    /// </summary>
    /// <remarks>
    /// The download is skipped when the item already carries the image this
    /// source would produce. Without that check every run re-fetched every
    /// performer's portrait from the remote CDNs — minutes of pointless
    /// traffic and repository churn on each daily pass, for bytes that are
    /// already on disk.
    /// </remarks>
    /// <param name="item">The collection receiving the poster.</param>
    /// <param name="ownerName">The performer the image belongs to.</param>
    /// <param name="source">Where the image comes from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored file path, or <c>null</c> when only a remote URL was attached.</returns>
    private async Task<string?> StoreImageAsync(BaseItem item, string ownerName, ImageSource source, CancellationToken ct)
    {
        try
        {
            var existing = item.GetImageInfo(ImageType.Primary, 0);
            if (existing is not null)
            {
                // Already showing this exact source: the file for this cache
                // key is present, or the remote URL matches.
                if (!string.IsNullOrWhiteSpace(existing.Path))
                {
                    var sameFile = existing.Path.EndsWith($"{source.CacheKey}.jpg", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(existing.Path);
                    var sameUrl = string.Equals(existing.Path, source.Url, StringComparison.OrdinalIgnoreCase);
                    if (sameFile || sameUrl)
                    {
                        return sameFile ? existing.Path : null;
                    }
                }
            }

            var stored = await DownloadImageAsync(item, ownerName, source, ct).ConfigureAwait(false);
            if (stored is not null)
            {
                item.SetImagePath(ImageType.Primary, stored);
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
                return stored;
            }

            // Download failed (or the CDN refused this server): attach the
            // remote URL so Jellyfin's own image pipeline can retry it.
            item.SetImage(new ItemImageInfo
            {
                Path = source.Url,
                Type = ImageType.Primary,
                DateModified = DateTime.UtcNow
            }, 0);
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not apply an image to '{Name}'", item.Name);
        }

        return null;
    }

    /// <summary>
    /// Downloads an image to the item's own image folder. The file name is
    /// stable per source, so re-running the task overwrites the same file
    /// instead of accumulating copies.
    /// </summary>
    /// <param name="item">The item that will own the file.</param>
    /// <param name="ownerName">The performer the image belongs to.</param>
    /// <param name="source">Where the image comes from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored file path, or <c>null</c> on failure.</returns>
    private async Task<string?> DownloadImageAsync(BaseItem item, string ownerName, ImageSource source, CancellationToken ct)
    {
        if (!JavHttp.IsUsableImageUrl(source.Url))
        {
            return null;
        }

        try
        {
            using var response = await JavHttp.GetImageResponse(source.Url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Image for '{Name}' answered HTTP {Status}",
                    ownerName,
                    (int)response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 512)
            {
                // Anything this small is an error page or a spacer, not a photo.
                return null;
            }

            var dir = item.GetInternalMetadataPath();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{source.CacheKey}.jpg");
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Downloading the image for '{Name}' failed", ownerName);
            return null;
        }
    }

    /// <summary>
    /// Gives the person item itself the same photo, so performer cards show
    /// a face in the library, in cast rows and in search results.
    /// </summary>
    /// <param name="personName">The performer to decorate.</param>
    /// <param name="source">The image to apply.</param>
    /// <param name="storedPath">A file downloaded for the collection, when one exists.</param>
    private void TrySetPersonItemImage(string personName, ImageSource source, string? storedPath)
    {
        try
        {
            var person = _libraryManager.GetPerson(personName);
            if (person is null)
            {
                return;
            }

            if (storedPath is not null)
            {
                person.SetImagePath(ImageType.Primary, storedPath);
                person.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                return;
            }

            if (person.HasImage(ImageType.Primary, 0))
            {
                return;
            }

            person.SetImage(new ItemImageInfo
            {
                Path = source.Url,
                Type = ImageType.Primary,
                DateModified = DateTime.UtcNow
            }, 0);
            person.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not set the library photo for '{Person}'", personName);
        }
    }

    // -----------------------------------------------------------------
    // Collection syncing
    // -----------------------------------------------------------------

    /// <summary>
    /// Creates the named collection when missing and replaces its content so
    /// it exactly mirrors the given movie ids, newest first. A box set that
    /// previously held nested collections (a gender card, or a person whose
    /// collection once contained links) is cleared first, so switching a
    /// collection between shapes never leaves stale members behind.
    /// </summary>
    /// <param name="name">Collection display name.</param>
    /// <param name="ids">Ordered movie ids for the collection.</param>
    /// <param name="overview">Overview text describing the collection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The synced box set, or <c>null</c> when creation failed.</returns>
    private async Task<BoxSet?> SyncMovieCollectionAsync(
        string name,
        IReadOnlyList<Guid> ids,
        string overview,
        CancellationToken ct)
    {
        var collection = await EnsureCollectionAsync(name, overview, ct).ConfigureAwait(false);
        if (collection is null)
        {
            return null;
        }

        await ClearNestedLinksAsync(collection, ct).ConfigureAwait(false);

        // Set-based diff: the All Videos collection holds the whole library,
        // and list-contains checks there are quadratic in the library size.
        var current = collection.GetLinkedChildren().Select(c => c.Id).ToHashSet();
        var wantedSet = ids.ToHashSet();
        var wanted = ids.Where(id => !current.Contains(id)).ToList();
        var removal = current.Where(id => !wantedSet.Contains(id)).ToList();

        if (removal.Count > 0)
        {
            await _collectionManager.RemoveFromCollectionAsync(collection.Id, removal).ConfigureAwait(false);
        }

        if (wanted.Count > 0)
        {
            await _collectionManager.AddToCollectionAsync(collection.Id, wanted).ConfigureAwait(false);
        }

        _logger.LogDebug("Synced collection '{Name}' to {Count} videos", name, ids.Count);
        return collection;
    }

    /// <summary>
    /// Creates or updates a browse card whose members are other
    /// collections: the card holds links to the performer collections, and
    /// opening it shows those performer cards.
    /// </summary>
    /// <param name="name">Card display name.</param>
    /// <param name="members">Ordered member collections with their ids.</param>
    /// <param name="overview">Overview text describing the card.</param>
    /// <param name="poster">Poster source for the card itself.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task SyncNestedCardAsync(
        string name,
        List<(string Name, Guid CollectionId)> members,
        string overview,
        ImageSource? poster,
        CancellationToken ct)
    {
        if (members.Count == 0)
        {
            // No members: retire the card rather than leaving an empty shell
            // in the collection view.
            DeleteCollectionByName(name);
            return;
        }

        var collection = await EnsureCollectionAsync(name, overview, ct).ConfigureAwait(false);
        if (collection is null)
        {
            return;
        }

        // Members are collections. They are written straight to the card's
        // link list: the collection manager's add path only accepts movies,
        // which is what previously turned these cards into long lists of
        // loose videos instead of performer collections.
        //
        // The child's ParentId is deliberately NOT written back. On Jellyfin
        // 12 that update deletes the box set (the item is removed as soon as
        // its parent changes), which emptied a freshly built wall while the
        // card still logged its members as linked. The card's linked children
        // are what Jellyfin resolves for the card's contents, so the
        // relationship lives here only.
        var links = new List<LinkedChild>(members.Count);
        foreach (var (_, memberId) in members)
        {
            var child = _libraryManager.GetItemById(memberId) as BoxSet;
            if (child is null)
            {
                continue;
            }

            links.Add(LinkedChild.Create(child));
        }

        if (links.Count == 0)
        {
            DeleteCollectionByName(name);
            return;
        }

        StripeUnchanged(collection, overview);

        // A card never holds movies of its own; drop any that an earlier
        // build left behind.
        var staleMovies = collection.GetLinkedChildren()
            .Where(c => c is Movie)
            .Select(c => c.Id)
            .ToList();
        if (staleMovies.Count > 0)
        {
            await _collectionManager.RemoveFromCollectionAsync(collection.Id, staleMovies).ConfigureAwait(false);
            _logger.LogInformation(
                "Card '{Name}': removed {Count} loose videos left by an earlier build",
                name,
                staleMovies.Count);
        }

        collection.LinkedChildren = [.. links];
        await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);

        if (poster is not null)
        {
            await StoreImageAsync(collection, name, poster, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "JavOrganizer card '{Name}': {Performers} performers linked",
            name,
            links.Count);
    }

    /// <summary>
    /// Builds the optional studio / genre / year collections from a
    /// grouping selector.
    /// </summary>
    /// <param name="items">All scraped videos.</param>
    /// <param name="groupSelector">Maps a video to its group names.</param>
    /// <param name="nameFormat">Composite name format, e.g. "Studio: {0}".</param>
    /// <param name="overviewFactory">Builds the overview from the group name and its ids.</param>
    /// <param name="minVideos">Minimum videos for the collection to exist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The collections written: display name, member ids and id.</returns>
    private async Task<List<(string Name, List<Guid> Ids, Guid CollectionId)>> SyncGroupedCollectionsAsync(
        List<Movie> items,
        Func<Movie, IEnumerable<string>> groupSelector,
        string nameFormat,
        Func<string, List<Guid>, string> overviewFactory,
        int minVideos,
        CancellationToken ct)
    {
        var byId = items.ToDictionary(i => i.Id);
        var groups = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            foreach (var group in groupSelector(item))
            {
                if (string.IsNullOrWhiteSpace(group))
                {
                    continue;
                }

                if (!groups.TryGetValue(group, out var list))
                {
                    list = [];
                    groups[group] = list;
                }

                list.Add(item.Id);
            }
        }

        var built = new List<(string Name, List<Guid> Ids, Guid CollectionId)>();
        var minimum = Math.Max(1, minVideos);
        foreach (var (group, ids) in groups)
        {
            ct.ThrowIfCancellationRequested();
            if (ids.Count < minimum)
            {
                continue;
            }

            var name = string.Format(System.Globalization.CultureInfo.InvariantCulture, nameFormat, group);
            var ordered = OrderByRelease(byId, ids);
            var collection = await SyncMovieCollectionAsync(name, ordered, overviewFactory(group, ids), ct).ConfigureAwait(false);
            if (collection is not null)
            {
                built.Add((name, ordered, collection.Id));
            }
        }

        return built;
    }

    /// <summary>
    /// Reports whether a collection is a performer collection that this run
    /// did not rebuild, and is therefore stale.
    /// </summary>
    /// <remarks>
    /// A collection this run created or reused is never retired, whatever its
    /// name looks like. The name check alone is not safe on a Jellyfin version
    /// that stores or reports collection names differently from the string the
    /// plugin passed in: the retirement pass then deleted collections the same
    /// run had just linked into a card, leaving the card pointing at nothing.
    /// </remarks>
    /// <param name="name">Collection display name.</param>
    /// <param name="id">Collection item id.</param>
    /// <param name="wantedNames">Performer collections this run rebuilt.</param>
    /// <param name="wantedIds">Their item ids.</param>
    /// <param name="knownPerformers">Names credited anywhere in the library.</param>
    /// <returns><c>true</c> when the collection is stale and should be deleted.</returns>
    internal static bool ShouldRetireCollection(
        string? name,
        Guid id,
        HashSet<string> wantedNames,
        HashSet<Guid> wantedIds,
        HashSet<string> knownPerformers)
        => !string.IsNullOrWhiteSpace(name)
        && !wantedIds.Contains(id)
        && !wantedNames.Contains(name)
        && (PersonPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || knownPerformers.Contains(name));

    /// <summary>
    /// Reports whether a performer's group has its own wall switched on. A
    /// credit whose gender cannot be resolved (typically a director) belongs
    /// to no wall, so it gets no collection: a prefix-less one would show up
    /// in neither browse card and look like an orphan.
    /// </summary>
    /// <param name="gender">"female", "male" or an empty string.</param>
    /// <param name="config">Current plugin configuration.</param>
    /// <returns><c>true</c> when a per-performer collection should be built.</returns>
    internal static bool IsPerformerGroupEnabled(string gender, PluginConfiguration config) => gender switch
    {
        "female" => config.BuildActressCollections,
        "male" => config.BuildActorCollections,
        _ => false
    };

    /// <summary>Projects performer entries onto a card's member list.</summary>
    /// <param name="members">Performers with their built collections.</param>
    /// <returns>The card's member tuples.</returns>
    private static List<(string Name, Guid CollectionId)> ToCardMembers(
        List<(string Name, PerformerEntry Entry)> members)
        => [.. members.Select(m => (m.Name, m.Entry.CollectionId))];

    /// <summary>
    /// Deletes the grouped collections (studio, genre or year) whose setting
    /// was just switched off, so the option removes what it used to build.
    /// </summary>
    /// <param name="prefix">The group's collection-name prefix.</param>
    private void RetireGroupedCollections(string prefix)
    {
        foreach (var collection in ListCollections())
        {
            if (collection.Name is { } name
                && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                DeleteCollection(collection);
            }
        }
    }

    /// <summary>
    /// Gets an existing collection by name, creating it when absent.
    /// </summary>
    /// <param name="name">Collection display name.</param>
    /// <param name="overview">Overview applied when the collection is created.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The box set, or <c>null</c> when it could not be created.</returns>
    private async Task<BoxSet?> EnsureCollectionAsync(string name, string overview, CancellationToken ct)
    {
        var existing = FindCollectionByName(name);
        if (existing is not null)
        {
            StripeUnchanged(existing, overview);
            return existing;
        }

        try
        {
            var created = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = name,
                IsLocked = true
            }).ConfigureAwait(false);

            StripeUnchanged(created, overview);
            await created.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
            return created;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create collection '{Name}'", name);
            return null;
        }
    }

    /// <summary>
    /// Applies the overview text only when it actually changed, so a
    /// recurring run does not churn the repository.
    /// </summary>
    /// <param name="collection">The collection.</param>
    /// <param name="overview">Desired overview text.</param>
    private static void StripeUnchanged(BoxSet collection, string overview)
    {
        if (!string.Equals(collection.Overview, overview, StringComparison.Ordinal))
        {
            collection.Overview = overview;
        }
    }

    /// <summary>
    /// Drops linked <i>collections</i> from a box set's link list, leaving
    /// movies alone. Used when a card is rebuilt: its previous performer links
    /// are replaced wholesale rather than diffed, because the wanted member
    /// list is the ordered set of performer collections.
    /// </summary>
    /// <remarks>
    /// Only the link list is rewritten. Writing a child's ParentId back is
    /// what made Jellyfin 12 delete the child box set, so the children are
    /// never touched here.
    /// </remarks>
    /// <param name="collection">The collection being cleared.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task ClearNestedLinksAsync(BoxSet collection, CancellationToken ct)
    {
        var nestedIds = collection.GetLinkedChildren()
            .Where(c => c is not Movie)
            .Select(c => c.Id)
            .ToHashSet();
        if (nestedIds.Count == 0)
        {
            return;
        }

        collection.LinkedChildren = collection.LinkedChildren
            .Where(l => l.ItemId is null || !nestedIds.Contains(l.ItemId.Value))
            .ToArray();
        await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes performer collections that no longer meet the configured
    /// minimum, so a re-scrape that changed a performer's name or dropped
    /// their videos does not leave an orphan card behind.
    /// </summary>
    /// <param name="byPerson">This run's performer entries.</param>
    /// <param name="minVideos">Configured minimum videos per collection.</param>
    /// <returns>The number of collections retired.</returns>
    private int RetireStalePersonCollections(Dictionary<string, PerformerEntry> byPerson, int minVideos)
    {
        var minimum = Math.Max(1, minVideos);
        var wanted = new HashSet<string>(
            byPerson
                .Where(kv => kv.Value.Items.Count >= minimum && kv.Value.CollectionId != Guid.Empty)
                .Select(kv => PrefixFor(kv.Value.Gender) + kv.Key),
            StringComparer.OrdinalIgnoreCase);

        var retired = 0;
        var wantedIds = new HashSet<Guid>(
            byPerson.Values
                .Where(kv => kv.Items.Count >= minimum && kv.CollectionId != Guid.Empty)
                .Select(kv => kv.CollectionId));

        // Names credited in this library, whatever their gender. Older builds
        // created prefix-less collections for credits with no resolvable
        // gender (mostly directors); those belong to no wall and are retired
        // here like any other stale performer collection.
        var knownPerformers = new HashSet<string>(byPerson.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var collection in ListCollections())
        {
            if (!ShouldRetireCollection(collection.Name, collection.Id, wanted, wantedIds, knownPerformers))
            {
                continue;
            }

            DeleteCollection(collection);
            retired++;
        }

        if (retired > 0)
        {
            _logger.LogInformation("Retired {Count} performer collections that no longer meet the minimum", retired);
        }

        return retired;
    }

    // -----------------------------------------------------------------
    // Library queries
    // -----------------------------------------------------------------

    /// <summary>
    /// Lists every movie that carries JavOrganizer metadata.
    /// </summary>
    /// <returns>All scraped movies in the library.</returns>
    private List<Movie> ListScrapedVideos()
    {
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie],
            IsFolder = false
        };

        var all = JavApiCompat.GetItemList(_libraryManager, query).ToList();
        return all
            .OfType<Movie>()
            .Where(m => m.ProviderIds.ContainsKey(JavMetadataProvider.ProviderIdKey))
            .ToList();
    }

    /// <summary>
    /// Lists every collection in the library.
    /// </summary>
    /// <returns>The box sets.</returns>
    private List<BoxSet> ListCollections()
    {
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.BoxSet]
        };

        return JavApiCompat.GetItemList(_libraryManager, query).OfType<BoxSet>().ToList();
    }

    /// <summary>
    /// Aggregates play counts and like scores for every scraped video across
    /// every user on the server.
    /// </summary>
    /// <param name="items">The scraped videos.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Per-item play counts and like scores.</returns>
    private Task<(Dictionary<Guid, int> Plays, Dictionary<Guid, double> Likes)> AggregateUserDataAsync(
        List<Movie> items,
        CancellationToken ct)
    {
        var playCounts = new Dictionary<Guid, int>();
        var likeScores = new Dictionary<Guid, double>();

        foreach (var raw in JavApiCompat.GetUsers(_userManager))
        {
            ct.ThrowIfCancellationRequested();
            // Bound at runtime: the user type's name and namespace are not
            // spelled out so the assembly stays valid on every Jellyfin
            // version the plugin supports.
            dynamic user = raw;
            foreach (var item in items)
            {
                var data = _userDataManager.GetUserData(user, item);
                if (data is null)
                {
                    continue;
                }

                playCounts.TryGetValue(item.Id, out var plays);
                playCounts[item.Id] = plays + data.PlayCount;

                var add = LikeScore(data.IsFavorite, data.Likes);
                if (add > 0)
                {
                    likeScores.TryGetValue(item.Id, out var score);
                    likeScores[item.Id] = score + add;
                }
            }
        }

        return Task.FromResult((playCounts, likeScores));
    }

    /// <summary>
    /// Scores one user's affinity for a video: a favorite counts 3, a like
    /// counts 2, and a user who did both contributes the sum. The two
    /// signals must be added together before writing — writing them one
    /// after the other let the like overwrite the favorite.
    /// </summary>
    /// <param name="isFavorite">Whether the user favorited the video.</param>
    /// <param name="likes">The user's like state, when set.</param>
    /// <returns>The combined score (0, 2, 3 or 5).</returns>
    internal static int LikeScore(bool isFavorite, bool? likes)
    {
        var score = 0;
        if (isFavorite)
        {
            score += 3;
        }

        if (likes == true)
        {
            score += 2;
        }

        return score;
    }

    /// <summary>
    /// Finds an existing box set by its exact name.
    /// </summary>
    /// <param name="name">Collection name.</param>
    /// <returns>The box set, or <c>null</c> when absent.</returns>
    private BoxSet? FindCollectionByName(string name)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Name = name
        };

        return JavApiCompat.GetItemList(_libraryManager, query)
            .OfType<BoxSet>()
            .FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Deletes a collection by exact name, used to retire old-style
    /// un-prefixed person collections and empty cards.
    /// </summary>
    /// <param name="name">Collection name.</param>
    private void DeleteCollectionByName(string name)
    {
        var existing = FindCollectionByName(name);
        if (existing is not null)
        {
            DeleteCollection(existing);
        }
    }

    /// <summary>
    /// Deletes a collection. Box sets carry no media of their own, so the
    /// delete target is the collection's own folder under the server's
    /// collections directory — never a movie. That folder is removed too:
    /// leaving it behind lets the library scan resurrect the collection on
    /// the next server start, which made switched-off groups come back.
    /// </summary>
    /// <param name="collection">The collection to delete.</param>
    private void DeleteCollection(BoxSet collection)
    {
        try
        {
            _libraryManager.DeleteItem(collection, new DeleteOptions
            {
                DeleteFileLocation = true
            });
            _logger.LogDebug("Deleted collection '{Name}'", collection.Name);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete collection '{Name}'", collection.Name);
        }
    }
}
