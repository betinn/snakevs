using SnakeVs.Core;

namespace SnakeVs.Game.Storage;

/// <summary>Loads and saves <see cref="SaveData"/> atomically (write temp file, then replace).</summary>
public sealed class SaveStore(string directory)
{
    private const string FileName = "snakevs.save";

    public string FilePath => Path.Combine(directory, FileName);

    public SaveData Load()
    {
        try
        {
            return File.Exists(FilePath) ? SaveData.Parse(File.ReadAllText(FilePath)) : new SaveData();
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
            Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, data.Serialize());
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Storage full or unavailable: keep playing, records stay in memory.
        }
    }
}
