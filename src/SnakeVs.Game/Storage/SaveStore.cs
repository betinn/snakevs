using SnakeVs.Core;

namespace SnakeVs.Game.Storage;

/// <summary>Where the serialized save lives. Implementations must never throw.</summary>
public interface ISaveStorage
{
    /// <summary>Returns the stored text, or null when nothing was saved yet or storage is unavailable.</summary>
    string? Read();

    void Write(string text);
}

/// <summary>Save file in a private app folder, written atomically (temp file, then replace). Android and desktop.</summary>
public sealed class FileSaveStorage(string directory) : ISaveStorage
{
    private const string FileName = "snakevs.save";

    public string FilePath => Path.Combine(directory, FileName);

    public string? Read()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Write(string text)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Storage full or unavailable: keep playing, records stay in memory.
        }
    }
}

/// <summary>Loads and saves <see cref="SaveData"/> through the platform storage.</summary>
public sealed class SaveStore(ISaveStorage storage)
{
    public SaveData Load()
    {
        try
        {
            return storage.Read() is { } text ? SaveData.Parse(text) : new SaveData();
        }
        catch (Exception)
        {
            return new SaveData();
        }
    }

    public void Save(SaveData data)
    {
        try
        {
            storage.Write(data.Serialize());
        }
        catch (Exception)
        {
            // Never let persistence break the game.
        }
    }
}
