using Microsoft.Data.Sqlite;
using MeetingTranscriber.Models;

namespace MeetingTranscriber.Services;

public class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;

    public DatabaseService(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Meetings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT,
                Transcript TEXT NOT NULL DEFAULT '',
                Summary TEXT NOT NULL DEFAULT ''
            )";
        cmd.ExecuteNonQuery();
    }

    public int CreateMeeting(Meeting meeting)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Meetings (Title, StartTime, EndTime, Transcript, Summary)
            VALUES (@title, @start, @end, @transcript, @summary);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@title", meeting.Title);
        cmd.Parameters.AddWithValue("@start", meeting.StartTime.ToString("o"));
        cmd.Parameters.AddWithValue("@end", (object?)meeting.EndTime?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@transcript", meeting.Transcript);
        cmd.Parameters.AddWithValue("@summary", meeting.Summary);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateMeeting(Meeting meeting)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE Meetings SET Title=@title, EndTime=@end, Transcript=@transcript, Summary=@summary
            WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", meeting.Id);
        cmd.Parameters.AddWithValue("@title", meeting.Title);
        cmd.Parameters.AddWithValue("@end", (object?)meeting.EndTime?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@transcript", meeting.Transcript);
        cmd.Parameters.AddWithValue("@summary", meeting.Summary);
        cmd.ExecuteNonQuery();
    }

    public List<Meeting> GetAllMeetings()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, StartTime, EndTime, Transcript, Summary FROM Meetings ORDER BY StartTime DESC";
        using var reader = cmd.ExecuteReader();
        var meetings = new List<Meeting>();
        while (reader.Read())
        {
            meetings.Add(new Meeting
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                StartTime = DateTime.Parse(reader.GetString(2)),
                EndTime = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
                Transcript = reader.GetString(4),
                Summary = reader.GetString(5)
            });
        }
        return meetings;
    }

    public Meeting? GetMeeting(int id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, StartTime, EndTime, Transcript, Summary FROM Meetings WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new Meeting
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                StartTime = DateTime.Parse(reader.GetString(2)),
                EndTime = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
                Transcript = reader.GetString(4),
                Summary = reader.GetString(5)
            };
        }
        return null;
    }

    public void DeleteMeeting(int id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Meetings WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
