using BaseBotLib.Interfaces.Logger;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToDoBot.DTO.Storage;
using ToDoBot.Interfaces.Storage;
using ToDoBot.Services.Bot;

namespace ToDoBot.Services.Storage
{
    public class ToDoInfoSqlLiteStorage : IInfoStorage
    {
        private readonly string _dbFilepath;
        private readonly ILogger _logger;

        public ToDoInfoSqlLiteStorage(string dbFilepath, ILogger logger)
        {
            _dbFilepath = dbFilepath;
            _logger = logger;
            
            Init();
        }

        private void Init()
        {
            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();
                var createUsersTable = new SqliteCommand(
                    "CREATE TABLE IF NOT EXISTS Users (Id INTEGER PRIMARY KEY, Data NVARCHAR(5000))", connection);
                createUsersTable.ExecuteNonQuery();

                var createRecordsTable = new SqliteCommand(
                    @"CREATE TABLE IF NOT EXISTS Records 
(Id GUID PRIMARY KEY, UserId INTEGER, Data NVARCHAR(2000) NULL, Date DATETIME NULL, IsArchive INTEGER, ArchiveDate DATETIME NULL, Duration INTEGER)", connection);
                createRecordsTable.ExecuteNonQuery();
            }
        }

        public async Task AddRecord(RecordData recordData)
        {
            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"INSERT INTO Records(Id, UserId, Data, Date, IsArchive, Duration) VALUES ($id, $userId, $data, $date, $isArchive, $duration)";
                command.Parameters.AddWithValue("$id", recordData.Id);
                command.Parameters.AddWithValue("$userId", recordData.UserId);
                command.Parameters.AddWithValue("$data", recordData.Data);
                command.Parameters.AddWithValue("$date", recordData.Date);
                command.Parameters.AddWithValue("$duration", recordData.Duration);
                command.Parameters.AddWithValue("$isArchive", 0);
                //command.Parameters.AddWithValue("$archiveDate", (DateTime?)null);

                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task UpdateRecord(int userId, Guid id, string text)
        {
            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"UPDATE Records SET Data = $data WHERE Id = $id AND userId = $userId";
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$userId", userId);
                command.Parameters.AddWithValue("$data", text);

                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task ArchiveRecords(int userId, DateTime archiveDate)
        {
            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"UPDATE Records SET IsArchive = 1, ArchiveDate = $archiveDate WHERE userId = $userId AND IsArchive = 0";
                command.Parameters.AddWithValue("$userId", userId);
                command.Parameters.AddWithValue("$archiveDate", archiveDate.Date);

                await command.ExecuteNonQueryAsync();
            }
        }
        public async Task<List<RecordData>> GetArchiveRecords(int userId, DateTime archiveDate)
        {
            var response = new List<RecordData>();

            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"SELECT id, Data, Date, Duration FROM Records WHERE userId = $userId AND ArchiveDate = $archiveDate";
                command.Parameters.AddWithValue("$userId", userId);
                command.Parameters.AddWithValue("$archiveDate", archiveDate.Date);

                using (var reader = command.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetGuid(0);
                        var data = reader.GetString(1);
                        var date = reader.GetDateTime(2);
                        var duration = reader.GetInt32(3);

                        response.Add(new RecordData
                        {
                            Id = id,
                            Date = date,
                            Data = data,
                            UserId = userId,
                            Duration = duration,
                        });
                    }
                }
            }

            return response;
        }
        public async Task<List<RecordData>> GetNewRecords(int userId)
        {
            var response = new List<RecordData>();

            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"SELECT id, Data, Date, Duration FROM Records WHERE userId = $userId AND IsArchive = 0";
                command.Parameters.AddWithValue("$userId", userId);

                using (var reader = command.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetGuid(0);
                        var data = reader.GetString(1);
                        var date = reader.GetDateTime(2);
                        var duration = reader.GetInt32(3);

                        response.Add(new RecordData
                        {
                            Id = id,
                            Date = date,
                            Data = data,
                            UserId = userId,
                            Duration = duration,
                        });
                    }
                }
            }

            return response;
        }
        public async Task<UserData> GetUser(int userId)
        {
            UserData response = null;

            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"SELECT Data FROM Users WHERE Id = $userId";
                command.Parameters.AddWithValue("$userId", userId);

                using (var reader = command.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var json = reader.GetString(0);
                        if (json == null)
                        {
                            return null;
                        }

                        response = JsonConvert.DeserializeObject<UserData>(json);
                    }
                }
            }

            return response;
        }

        public async Task<List<int>> GetUserIds()
        {
            var response = new List<int>();

            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"SELECT Id FROM Users";

                using (var reader = command.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetInt32(0);
                        response.Add(id);
                    }
                }
            }

            return response;
        }

        public async Task UpdateUser(UserData userData)
        {
            var json = JsonConvert.SerializeObject(userData);

            using (var connection = new SqliteConnection($"Filename={_dbFilepath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"INSERT INTO Users(Id, Data) VALUES($id, $data)
  ON CONFLICT(id) DO UPDATE SET data=excluded.data";
                command.Parameters.AddWithValue("$id", userData.UserId);
                command.Parameters.AddWithValue("$data", json);

                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
