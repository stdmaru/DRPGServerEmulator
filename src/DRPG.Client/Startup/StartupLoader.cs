using System.Collections.Concurrent;
using DRPG.Client.Text;
using DRPG.Engine.Assets;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Client.Startup;

/// <summary>
/// The real work behind the loading screen, in the order of the original (docs/cliente/carga-inicial.md): the loading
/// screen sprites (target 39), the 39 table groups with their language files (70), the managers (90, no files), the
/// sprite lists of <c>Game Init</c> (99) and the lists of <c>CSceneTitle</c> (101). Everything is read and decoded
/// for real and there is no artificial wait: how long the bar takes is how long this takes. Runs on a pool thread and
/// reports to <see cref="LoadingProgress"/>; the screen only reads the progress and, at the end, the result.
/// </summary>
public sealed class StartupLoader(GameInstall install, PackArchive resource, LoadingProgress progress, Action<string> log)
{
    // ✅ the targets of 0x00b34d40, in the order they are raised
    public const float AfterLoadingGui = 39f, AfterTables = 70f, AfterManagers = 90f, AfterGameInit = 99f, AfterTitle = 101f;

    /// <summary>✅ <c>0x00b231b0</c>: icons 1..1099 (<c>Sprite\icon_%03d.spr</c>), those that exist.</summary>
    public const int FirstIcon = 1, LastIcon = 1099;

    /// <summary>Sprites whose data is at most this big stay parsed for the scenes; bigger ones are only decoded.</summary>
    public const int MaxKeptBytes = 1 << 20;

    /// <summary>Workers of the sprite lists (see <see cref="PreloadAll"/>).</summary>
    public static readonly int Parallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    private readonly ConcurrentDictionary<string, SpriteFile> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _skippedFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Listed files the pack does not have (skipped, like the original).</summary>
    public IReadOnlyCollection<string> SkippedFiles => _skippedFiles.Keys.ToArray();

    public Task<GameData> RunAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

    private GameData Run(CancellationToken ct)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        // The resource pack is open; a pack with no entries is a wrong install and fails here, not later.
        if (resource.Names.Count == 0)
            throw new PackFormatException("resource.hf has no entries");

        PreloadAll(PreloadList.Read(PreloadList.LoadingScreenGui), ct);
        progress.Raise(AfterLoadingGui);
        log($"load: screen GUI {timer.ElapsedMilliseconds} ms");

        var table = PackArchive.Open(install.TablePath);
        var (tables, language, texts, prohibitedWords) = LoadTables(table, ct);
        progress.Raise(AfterTables);
        log($"load: tables {timer.ElapsedMilliseconds} ms");

        // ✅ ~50 manager constructors in the original read no files: nothing to do but report it
        progress.Raise(AfterManagers);

        PreloadAll(PreloadList.Read(PreloadList.GameInit), ct);
        progress.Raise(AfterGameInit);
        log($"load: game init lists {timer.ElapsedMilliseconds} ms");

        PreloadAll(PreloadList.Read(PreloadList.Title), ct);
        PreloadAll(Enumerable.Range(FirstIcon, LastIcon - FirstIcon + 1).Select(i => $"Sprite\\icon_{i:000}.spr"), ct);
        progress.Raise(AfterTitle);

        log($"load: {_sprites.Count} sprites, {tables.Count} tables in {timer.ElapsedMilliseconds} ms");
        if (SkippedFiles.Count > 0)
            log($"load: {SkippedFiles.Count} listed files are not in resource.hf and were skipped");
        return new GameData(resource, table, tables, language, texts, prohibitedWords, _sprites);
    }

    private (Dictionary<string, IReadOnlyList<string[]>> Tables, Dictionary<string, byte[]> Language, TextAttach Texts, ProhibitedWords ProhibitedWords) LoadTables(PackArchive table, CancellationToken ct)
    {
        var tables = new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.OrdinalIgnoreCase);
        var language = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var languageFiles = Directory.Exists(install.LanguageDirectory)
            ? Directory.GetFiles(install.LanguageDirectory, "*.bin").ToDictionary(file => Path.GetFileName(file)!, file => file, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in TableList.Groups.SelectMany(group => group))
        {
            ct.ThrowIfCancellationRequested();
            if (table.Contains(TableList.TablePath(name)))
                tables[name] = table.ReadTable(TableList.TablePath(name));
            else
                _skippedFiles.TryAdd(TableList.TablePath(name), 0);
            var languageFile = TableList.LanguageFile(name);
            if (languageFiles.TryGetValue(languageFile, out var path))
                language[languageFile] = File.ReadAllBytes(path);
        }

        var texts = new TextAttach();
        if (tables.TryGetValue("TextAttach", out var rows))
            texts.AddTableRows(rows);
        if (language.TryGetValue(TableList.LanguageFile("TextAttach"), out var bytes))
            texts.AddLanguageFile(bytes);

        var prohibitedWords = new ProhibitedWords();
        if (tables.TryGetValue("ProhibitedWords", out var prohibitedRows))
            prohibitedWords.AddTableRows(prohibitedRows);
        if (language.TryGetValue(TableList.LanguageFile("ProhibitedWords"), out var prohibitedBytes))
            prohibitedWords.AddLanguageFile(prohibitedBytes);
        return (tables, language, texts, prohibitedWords);
    }

    /// <summary>
    /// Reads and decodes the listed files on several threads. ❓ The original does it on one thread and takes about 10 s;
    /// this inflates and decodes the same files (6 GB of sprite data, most of it the monsters of the title list), so on
    /// one thread it takes about 27 s on a desktop CPU. A few workers bring it back to the original's order of magnitude
    /// without any waiting; <see cref="Parallelism"/> = 1 gives the original's strict one-thread work.
    /// </summary>
    private void PreloadAll(IEnumerable<string> paths, CancellationToken ct)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct };
        Parallel.ForEach(paths, options, () => Array.Empty<uint>(), (path, _, scratch) => Preload(path, scratch), _ => { });
    }

    /// <summary>
    /// Reads one listed file and decodes it (✅ "load" = read from <c>resource.hf</c> and decode every frame). A file
    /// the pack lacks is skipped, like the original; a file that is in the pack and broken is an error. A <c>.ani</c>
    /// loads the sprite it names too. Returns the scratch buffer, grown when the frame needed more.
    /// </summary>
    private uint[] Preload(string path, uint[] scratch)
    {
        if (_sprites.ContainsKey(path))
            return scratch;
        if (!resource.Contains(path))
        {
            _skippedFiles.TryAdd(path, 0);
            return scratch;
        }
        if (path.EndsWith(".ani", StringComparison.OrdinalIgnoreCase))
        {
            var ani = AniFile.Parse(resource.ReadEntry(path));
            return Preload(path[..(path.LastIndexOf('\\') + 1)] + ani.SpriteName + ".spr", scratch);
        }
        var data = resource.ReadEntry(path);
        var sprite = SpriteFile.Parse(data);
        for (int frame = 0; frame < sprite.Frames.Count; frame++)
        {
            var info = sprite.Frames[frame];
            int size = info.Width * info.Height;
            if (scratch.Length < size)
                scratch = new uint[size];
            sprite.Decode(sprite.Image, frame, scratch);
        }
        // The big ones (monsters, effects) are decoded for the work's sake and dropped: kept, they would be gigabytes.
        if (data.Length <= MaxKeptBytes)
            _sprites[path] = sprite;
        return scratch;
    }
}
