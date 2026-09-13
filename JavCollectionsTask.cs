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
/// Scheduled task that maintains Jellyfin collections from the scraped
/// library:
/// <list type="bullet">
/// <item>one collection per actress and per male actor, with the person's
/// photo as the collection image and a role-tagged overview,</item>
/// <item>"Most Viewed" and "Most Liked" rankings aggregated across every
/// user,</item>
/// <item>optional collections per studio, genre and release year.</item>
/// </list>
/// Runs daily after the cache cleanup so the collections reflect fresh
/// scrapes.
/// </summary>
public sealed class JavCollectionsTask : IScheduledTask, IConfigurableScheduledTask
{
    private const string MostViewedName = "Most Viewed (JavOrganizer)";
    private const string MostLikedName = "Most Liked (JavOrganizer)";

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
    public string Description => "Maintains per-actress and per-actor collections (with photos), Most Viewed / Most Liked rankings, and optional studio, genre and year collections.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.EffectiveConfiguration;
        if (!config.BuildCollections)
        {
            _logger.LogInformation("JavOrganizer collections are disabled in the plugin settings");
            return;
        }

        var items = ListScrapedVideos(progress);
        if (items.Count == 0)
        {
            _logger.LogInformation("No JavOrganizer-scraped videos found; nothing to collect");
            return;
        }

        // Gender knowledge comes from the plugin's own scrape cache: each
        // cached record holds separated female/male cast lists, which is the
        // only reliable gender source (Jellyfin people carry no gender).
        var genderByPerson = JavCache.LoadGenderIndex(_logger);

        // ---- People collections, gender-tagged ----
        // Names become "Actress: AIKA" / "Actor: Yuzuru Yuuki" so the
        // library groups them visibly, and old un-prefixed duplicates from
        // previous runs are retired.
        progress.Report(15);
        var byPerson = new Dictionary<string, (List<Guid> Items, string Gender, string Role)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            foreach (var person in _libraryManager.GetPeople(item))
            {
                if (string.IsNullOrWhiteSpace(person.Name))
                {
                    continue;
                }

                var gender = genderByPerson.TryGetValue(person.Name, out var known)
                    ? known
                    : GuessGender(person.Name, item);
                var role = DescribeRole(person, gender);
                if (!byPerson.TryGetValue(person.Name, out var entry))
                {
                    entry = ([], gender, role);
                    byPerson[person.Name] = entry;
                }

                entry.Items.Add(item.Id);
            }
        }

        var peopleBuilt = 0;
        var retired = new List<string>();
        foreach (var (person, entry) in byPerson)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Items.Count < config.MinVideosPerCollection)
            {
                continue;
            }

            // Release-date ordering inside every person collection.
            var ordered = OrderByRelease(items, entry.Items);

            var prefix = entry.Gender switch
            {
                "female" => "Actress: ",
                "male" => "Actor: ",
                _ => string.Empty
            };
            var collectionName = prefix + person;
            var overview = $"{entry.Role} — appears in {entry.Items.Count} videos in this library.";

            await SyncCollectionAsync(collectionName, ordered, cancellationToken, overview, person).ConfigureAwait(false);
            peopleBuilt++;

            // Retire the old-style un-prefixed collection when present.
            if (prefix.Length > 0)
            {
                retired.Add(person);
            }

            progress.Report(15 + (25.0 * peopleBuilt / Math.Max(1, byPerson.Count)));
        }

        foreach (var oldName in retired)
        {
            DeleteCollectionByName(oldName);
        }

        // ---- Library-wide play / like stats, aggregated over all users ----
        progress.Report(50);
        var playCounts = new Dictionary<Guid, int>();
        var likeScores = new Dictionary<Guid, double>();
#if JF_LEGACY_TASK_TRIGGER
        var users = _userManager.Users;
#else
        var users = _userManager.GetUsers();
#endif
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in items)
            {
                var data = _userDataManager.GetUserData(user, item);
                if (data is null)
                {
                    continue;
                }

                playCounts.TryGetValue(item.Id, out var plays);
                playCounts[item.Id] = plays + data.PlayCount;

                likeScores.TryGetValue(item.Id, out var score);
                if (data.IsFavorite)
                {
                    likeScores[item.Id] = score + 3;
                }

                if (data.Likes == true)
                {
                    likeScores[item.Id] = score + 2;
                }
            }
        }

        // ---- Most Viewed ----
        progress.Report(60);
        var mostViewed = playCounts
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(100)
            .Select(kv => kv.Key)
            .ToList();
        if (mostViewed.Count > 0)
        {
            await SyncCollectionAsync(MostViewedName, mostViewed, cancellationToken, "Top 100 most played videos across all users.", null).ConfigureAwait(false);
        }

        // ---- Most Liked ----
        progress.Report(65);
        var mostLiked = likeScores
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(100)
            .Select(kv => kv.Key)
            .ToList();
        if (mostLiked.Count > 0)
        {
            await SyncCollectionAsync(MostLikedName, mostLiked, cancellationToken, "Top 100 videos by likes and favorites across all users.", null).ConfigureAwait(false);
        }

        // ---- All Videos (full library, release-date sorted) ----
        progress.Report(68);
        var allOrdered = OrderByRelease(items, items.Select(i => i.Id).ToList());
        await SyncCollectionAsync(AllVideosName, allOrdered, cancellationToken, $"Every scraped video in this library ({allOrdered.Count} titles), newest first.", null).ConfigureAwait(false);

        // ---- Newest Releases (top 100 by release date) ----
        progress.Report(70);
        var newest = allOrdered.Take(100).ToList();
        await SyncCollectionAsync(NewestReleasesName, newest, cancellationToken, "The 100 most recently released videos in this library.", null).ConfigureAwait(false);

        // ---- Studio collections ----
        var groupsBuilt = 0;
        if (config.BuildStudioCollections)
        {
            progress.Report(70);
            var byStudio = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                foreach (var studio in item.Studios)
                {
                    if (string.IsNullOrWhiteSpace(studio))
                    {
                        continue;
                    }

                    if (!byStudio.TryGetValue(studio, out var list))
                    {
                        list = [];
                        byStudio[studio] = list;
                    }

                    list.Add(item.Id);
                }
            }

            foreach (var (studio, ids) in byStudio)
            {
                if (ids.Count < config.MinVideosPerCollection)
                {
                    continue;
                }

                await SyncCollectionAsync($"Studio: {studio}", ids, cancellationToken, $"All {ids.Count} videos from studio {studio} in this library.", null).ConfigureAwait(false);
                groupsBuilt++;
            }
        }

        // ---- Genre collections ----
        if (config.BuildGenreCollections)
        {
            progress.Report(80);
            var byGenre = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                foreach (var genre in item.Genres)
                {
                    if (string.IsNullOrWhiteSpace(genre))
                    {
                        continue;
                    }

                    if (!byGenre.TryGetValue(genre, out var list))
                    {
                        list = [];
                        byGenre[genre] = list;
                    }

                    list.Add(item.Id);
                }
            }

            foreach (var (genre, ids) in byGenre)
            {
                if (ids.Count < config.MinVideosPerCollection)
                {
                    continue;
                }

                await SyncCollectionAsync($"Genre: {genre}", ids, cancellationToken, $"{ids.Count} {genre} videos in this library.", null).ConfigureAwait(false);
                groupsBuilt++;
            }
        }

        // ---- Year collections ----
        if (config.BuildYearCollections)
        {
            progress.Report(90);
            var byYear = items
                .Where(i => i.ProductionYear is > 1900)
                .GroupBy(i => i.ProductionYear!.Value)
                .OrderByDescending(g => g.Key);
            foreach (var year in byYear)
            {
                var ids = year.Select(i => i.Id).ToList();
                if (ids.Count < config.MinVideosPerCollection)
                {
                    continue;
                }

                await SyncCollectionAsync($"Year: {year.Key}", ids, cancellationToken, $"{ids.Count} videos released in {year.Key}.", null).ConfigureAwait(false);
                groupsBuilt++;
            }
        }

        _logger.LogInformation(
            "JavOrganizer collections updated: {People} people collections, {Groups} studio/genre/year collections, {Viewed} most-viewed, {Liked} most-liked, {Total} all-videos",
            peopleBuilt,
            groupsBuilt,
            mostViewed.Count,
            mostLiked.Count,
            allOrdered.Count);
        progress.Report(100);
    }

    private const string AllVideosName = "All Videos (JavOrganizer)";
    private const string NewestReleasesName = "Newest Releases (JavOrganizer)";

    private static readonly Microsoft.Extensions.Logging.Abstractions.NullLogger NullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <summary>
    /// Orders item ids by their release date, newest first, so every
    /// collection is browsable chronologically. Items without a date sink
    /// to the end in name order.
    /// </summary>
    /// <param name="items">All scraped movies.</param>
    /// <param name="ids">The ids to order.</param>
    /// <returns>Release-date-ordered ids.</returns>
    private static List<Guid> OrderByRelease(List<Movie> items, List<Guid> ids)
    {
        var byId = items.ToDictionary(i => i.Id);
        return ids
            .Where(id => byId.ContainsKey(id))
            .OrderByDescending(id => byId[id].PremiereDate ?? DateTime.MinValue)
            .ThenBy(id => byId[id].SortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        var record = JavCache.TryReadForItem(item.Path, item.Name, NullLogger);
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
        #if JF_LEGACY_PERSON
        var isDirector = (person.Type as string ?? string.Empty).Equals("Director", StringComparison.OrdinalIgnoreCase);
#else
        var isDirector = person.Type == PersonKind.Director;
#endif
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
    /// Deletes a collection by exact name, used to retire old-style
    /// un-prefixed person collections after gender-prefixed ones replace
    /// them.
    /// </summary>
    /// <param name="name">Collection name.</param>
    private void DeleteCollectionByName(string name)
    {
        try
        {
            var existing = FindCollectionByName(name);
            if (existing is null)
            {
                return;
            }

            // Collections contain no media of their own, so deleting them
            // must never touch the underlying files.
            _libraryManager.DeleteItem(existing, new MediaBrowser.Controller.Library.DeleteOptions
            {
                DeleteFileLocation = false
            });
            _logger.LogDebug("Retired old-style collection '{Name}'", name);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not retire old collection '{Name}'", name);
        }
    }

    /// <summary>
    /// Lists every movie that carries JavOrganizer metadata.
    /// </summary>
    /// <param name="progress">Progress reporter.</param>
    /// <returns>All scraped movies in the library.</returns>
    private List<Movie> ListScrapedVideos(IProgress<double> progress)
    {
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie],
            IsFolder = false
        };

        var all = _libraryManager.GetItemList(query) ?? [];
        var scraped = all
            .OfType<Movie>()
            .Where(m => m.ProviderIds.TryGetValue(JavMetadataProvider.ProviderIdKey, out _))
            .ToList();
        progress.Report(10);
        return scraped;
    }

    /// <summary>
    /// Creates the named collection when missing, then replaces its content
    /// so the collection exactly mirrors the given item ids. Person
    /// collections additionally copy the person's photo as the collection
    /// image and carry a role-tagged overview.
    /// </summary>
    /// <param name="name">Collection display name.</param>
    /// <param name="ids">Ordered item ids for the collection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="overview">Overview text describing the collection.</param>
    /// <param name="personName">When set, the person whose photo becomes the collection image.</param>
    private async Task SyncCollectionAsync(string name, IReadOnlyList<Guid> ids, CancellationToken ct, string overview, string? personName)
    {
        var existing = FindCollectionByName(name);
        BoxSet collection;
        if (existing is null)
        {
            var created = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = name,
                IsLocked = true,
                ItemIdList = ids.Select(id => id.ToString("N", System.Globalization.CultureInfo.InvariantCulture)).ToList()
            }).ConfigureAwait(false);
            collection = created;
        }
        else
        {
            collection = existing;

            // Replace membership: remove everything, then add the fresh set,
            // so renamed/deleted items drop out automatically.
            var current = collection.Children
                .Select(c => c.Id)
                .ToList();
            if (current.Count > 0)
            {
                await _collectionManager.RemoveFromCollectionAsync(collection.Id, current).ConfigureAwait(false);
            }

            await _collectionManager.AddToCollectionAsync(collection.Id, ids).ConfigureAwait(false);
        }

        // Refinements: overview text and, for person collections, the
        // person's own photo as the collection poster.
        var changed = false;
        if (!string.Equals(collection.Overview, overview, StringComparison.Ordinal))
        {
            collection.Overview = overview;
            changed = true;
        }

        if (personName is not null && !collection.HasImage(ImageType.Primary))
        {
            TryCopyPersonImage(collection, personName);
            changed = true;
        }

        if (changed)
        {
            await collection.UpdateToRepositoryAsync(MediaBrowser.Controller.Library.ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
        }

        _logger.LogDebug("Synced collection '{Name}' to {Count} items", name, ids.Count);
    }

    /// <summary>
    /// Copies the person's primary photo onto the collection, when the
    /// person item exists and has an image.
    /// </summary>
    /// <param name="collection">The collection receiving the image.</param>
    /// <param name="personName">The person whose photo to copy.</param>
    private void TryCopyPersonImage(BoxSet collection, string personName)
    {
        try
        {
            var person = _libraryManager.GetPerson(personName);
            var imagePath = person?.PrimaryImagePath;
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return;
            }

            collection.SetImagePath(ImageType.Primary, imagePath);
            _logger.LogDebug("Applied {Person}'s photo to their collection", personName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not copy person image for '{Person}'", personName);
        }
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

        return _libraryManager.GetItemList(query)?
            .OfType<BoxSet>()
            .FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
#if JF_LEGACY_TASK_TRIGGER
            Type = "DailyTrigger",
#else
            Type = TaskTriggerInfoType.DailyTrigger,
#endif
            // After the cache cleanup, so people from fresh scrapes are in.
            TimeOfDayTicks = TimeSpan.FromHours(4).Add(TimeSpan.FromMinutes(30)).Ticks
        };
    }
}
