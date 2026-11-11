using Microsoft.Data.Sqlite;
using SnakeVs.Core;

namespace SnakeVs.Server;

/// <summary>
/// Online ranking stored in a single SQLite file. One connection per call (pooled by Microsoft.Data.Sqlite);
/// WAL mode lets reads run while a score is being written.
/// </summary>
public sealed class ScoreStore
{
    private readonly string _connectionString;

    public ScoreStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            DefaultTimeout = 10,
        }.ToString();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS scores (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                mode        INTEGER NOT NULL,
                difficulty  INTEGER NOT NULL,
                name        TEXT    NOT NULL,
                score       INTEGER NOT NULL,
                apples      INTEGER NOT NULL,
                level       INTEGER NOT NULL,
                steps       INTEGER NOT NULL,
                created_utc TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_scores_board ON scores (mode, difficulty, score DESC, id);
            """;
        command.ExecuteNonQuery();
    }

    public async Task<SubmitResult> AddAsync(ScoreSubmission s, string name, CancellationToken ct)
    {
        await using var connection = Open();
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO scores (mode, difficulty, name, score, apples, level, steps, created_utc)
            VALUES ($mode, $difficulty, $name, $score, $apples, $level, $steps, $created);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$mode", (int)s.Mode);
        insert.Parameters.AddWithValue("$difficulty", (int)s.Difficulty);
        insert.Parameters.AddWithValue("$name", name);
        insert.Parameters.AddWithValue("$score", s.Score);
        insert.Parameters.AddWithValue("$apples", s.Apples);
        insert.Parameters.AddWithValue("$level", s.Level);
        insert.Parameters.AddWithValue("$steps", s.Steps);
        insert.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        var id = (long)(await insert.ExecuteScalarAsync(ct))!;

        // Ties go to whoever got there first (lower id).
        await using var rank = connection.CreateCommand();
        rank.Transaction = tx;
        rank.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM scores WHERE mode = $mode AND difficulty = $difficulty
                    AND (score > $score OR (score = $score AND id < $id))) + 1,
                (SELECT COUNT(*) FROM scores WHERE mode = $mode AND difficulty = $difficulty);
            """;
        rank.Parameters.AddWithValue("$mode", (int)s.Mode);
        rank.Parameters.AddWithValue("$difficulty", (int)s.Difficulty);
        rank.Parameters.AddWithValue("$score", s.Score);
        rank.Parameters.AddWithValue("$id", id);
        await using var reader = await rank.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var result = new SubmitResult(id, reader.GetInt32(0), reader.GetInt32(1));
        await reader.CloseAsync();

        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<ScoreEntry[]> TopAsync(GameMode mode, Difficulty difficulty, int take, CancellationToken ct)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, score, apples, level, created_utc FROM scores
            WHERE mode = $mode AND difficulty = $difficulty
            ORDER BY score DESC, id
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$mode", (int)mode);
        command.Parameters.AddWithValue("$difficulty", (int)difficulty);
        command.Parameters.AddWithValue("$take", take);

        var list = new List<ScoreEntry>(take);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ScoreEntry(
                reader.GetInt64(0),
                list.Count + 1,
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }

        return [.. list];
    }

    /// <summary>Removes one entry (moderation). Returns false when it did not exist.</summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM scores WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
