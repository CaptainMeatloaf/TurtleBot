using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TurtleBot.Services
{
    public class DatabaseService
    {
        private readonly ILogger logger;
        private readonly IConfiguration config;

        public DatabaseService(ILoggerFactory loggerFactory, IConfiguration config)
        {
            this.logger = loggerFactory.CreateLogger("database");
            this.config = config;

            using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
            {
                db.Open();
                String tagTableCommand = "CREATE TABLE IF NOT EXISTS Tags (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL COLLATE NOCASE, CurrentRevisionId INTEGER NOT NULL)";
                SqliteCommand createTagTable = new SqliteCommand(tagTableCommand, db);

                String tagRevisionsTableCommand = "CREATE TABLE IF NOT EXISTS TagRevisions (Id INTEGER PRIMARY KEY AUTOINCREMENT, TagId INTEGER NOT NULL, EditTime DATETIME NOT NULL, Content TEXT NOT NULL, EditorUserId TEXT NOT NULL)";
                SqliteCommand createTagRevisionsTable = new SqliteCommand(tagRevisionsTableCommand, db);

                try
                {
                    createTagTable.ExecuteReader();
                    createTagRevisionsTable.ExecuteReader();
                }
                catch (SqliteException e)
                {
                    logger.LogCritical($"SQLite Exception creating the tables: {e.Message}");
                }
            }
        }

        #region Tags

        public bool InsertTag(string name, string content, SocketUser user)
        {
            using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
            {
                db.Open();
                using (SqliteTransaction transaction = db.BeginTransaction())
                {
                    SqliteCommand insertTagCommand = new SqliteCommand("INSERT INTO Tags (Name, CurrentRevisionId) VALUES (@name, 0)", db, transaction);
                    insertTagCommand.Parameters.Add(new SqliteParameter("@name", name));

                    SqliteCommand insertTagHistoryCommand = new SqliteCommand("INSERT INTO TagRevisions (TagId, EditTime, Content, EditorUserId) VALUES (last_insert_rowid(), @editTime, @content, @editorUserId)", db, transaction);
                    insertTagHistoryCommand.Parameters.Add(new SqliteParameter("@editTime", DateTime.UtcNow.ToString("O")));
                    insertTagHistoryCommand.Parameters.Add(new SqliteParameter("@content", content));
                    insertTagHistoryCommand.Parameters.Add(new SqliteParameter("@editorUserId", user.Id.ToString()));

                    SqliteCommand updateTagCommand = new SqliteCommand("UPDATE Tags SET CurrentRevisionId = last_insert_rowid() WHERE Id = (SELECT MAX(Id) from Tags)", db, transaction);

                    try
                    {
                        insertTagCommand.ExecuteNonQuery();
                        insertTagHistoryCommand.ExecuteNonQuery();
                        updateTagCommand.ExecuteNonQuery();
                        transaction.Commit();
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite error inserting tag: {e.Message}");
                        return false;
                    }

                    logger.LogInformation($"User {user.Username}#{user.Discriminator} ({user.Id}) added tag \"{name}\" with content \"{content}\"");

                    return true;
                }
            }
        }

        public bool UpdateTag(string name, string newContent, SocketUser user)
        {
            using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
            {
                db.Open();

                SqliteCommand selectTagToUpdateCommand = new SqliteCommand("SELECT Id FROM Tags WHERE Name = @name", db);
                selectTagToUpdateCommand.Parameters.Add(new SqliteParameter("@name", name));

                long tagToUpdateId = 0;

                try
                {
                    var result = selectTagToUpdateCommand.ExecuteScalar();
                    if (result == null)
                    {
                        logger.LogError($"User {user.Id} attempted up update nonexistent tag {name}");
                        return false;
                    }
                    else
                    {
                        tagToUpdateId = (long)result;
                    }
                }
                catch (SqliteException e)
                {
                    logger.LogError($"SQLite error updating tag: {e.Message}");
                    return false;
                }

                using (SqliteTransaction transaction = db.BeginTransaction())
                {
                    SqliteCommand insertTagHistoryCommand = new SqliteCommand("INSERT INTO TagRevisions (TagId, EditTime, Content, EditorUserId) VALUES (@tagId, @editTime, @content, @editorUserId)", db, transaction);
                    insertTagHistoryCommand.Parameters.Add(new SqliteParameter("@tagId", tagToUpdateId));
                    insertTagHistoryCommand.Parameters.Add(new SqliteParameter("@editTime", DateTime.UtcNow.ToString("O")));
                    insertTagHistoryCommand.Parameters.Add(new SqliteParameter("@content", newContent));
                    insertTagHistoryCommand.Parameters.Add(new SqliteParameter("@editorUserId", user.Id.ToString()));

                    SqliteCommand updateTagCommand = new SqliteCommand("UPDATE Tags SET CurrentRevisionId = last_insert_rowid() WHERE Id = @tagToUpdateId", db, transaction);
                    updateTagCommand.Parameters.Add(new SqliteParameter("@tagToUpdateId", tagToUpdateId));

                    try
                    {
                        insertTagHistoryCommand.ExecuteNonQuery();
                        updateTagCommand.ExecuteNonQuery();
                        transaction.Commit();
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite error updating tag: {e.Message}");
                        return false;
                    }

                    logger.LogInformation($"User {user.Username}#{user.Discriminator} ({user.Id}) updated tag \"{name}\" to content \"{newContent}\"");

                    return true;
                }
            }
        }

        public bool DeleteTag(string name, SocketUser user)
        {
            // Update the revision to reflect the name of the tag before it was deleted
            if (!this.UpdateTag(name, name, user))
            {
                logger.LogError($"Failed to update the revision to indicate deletion of command \"{name}\" requested by {user.Username}#{user.Discriminator} ({user.Id})");
                return false;
            }

            using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
            {
                db.Open();

                SqliteCommand deleteTagCommand = new SqliteCommand("DELETE FROM Tags WHERE Name = @name", db);
                deleteTagCommand.Parameters.Add(new SqliteParameter("@name", name));

                try
                {
                    deleteTagCommand.ExecuteScalar();
                }
                catch (SqliteException e)
                {
                    logger.LogError($"SQLite error deleting tag: {e.Message}");
                    return false;
                }

                logger.LogInformation($"User {user.Username}#{user.Discriminator} ({user.Id}) deleted tag \"{name}\"");

                return true;
            }
        }

        public List<String> GetTagList()
        {
            using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
            {
                db.Open();

                SqliteCommand selectAllTagsCommand = new SqliteCommand("SELECT Name FROM Tags", db);

                List<String> tagList = new List<String>();

                try
                {
                    var tagListReader = selectAllTagsCommand.ExecuteReader();

                    while (tagListReader.Read())
                    {
                        tagList.Add(tagListReader[0].ToString());
                    }
                }
                catch (SqliteException e)
                {
                    logger.LogError($"SQLite Exception retrieving tag list: {e.Message}");
                }

                return tagList;
            }
        }

        public string GetTag(string name)
        {
            using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
            {
                db.Open();

                SqliteCommand selectAllTagsCommand = new SqliteCommand("SELECT TagRevisions.Content FROM TagRevisions INNER JOIN Tags ON Tags.CurrentRevisionId = TagRevisions.Id WHERE Tags.Name = @name", db);
                selectAllTagsCommand.Parameters.Add(new SqliteParameter("@name", name));

                string content = null;

                try
                {
                    var result = selectAllTagsCommand.ExecuteScalar();
                    content = result?.ToString();
                    if (String.IsNullOrWhiteSpace(content))
                    {
                        logger.LogError($"Tag \"{name}\" gave empty or null content. Possible partial deletion.");
                        return null;
                    }
                }
                catch (SqliteException e)
                {
                    logger.LogError($"SQLite Exception retrieving tag content: {e.Message}");
                }

                return content;
            }
        }

        #endregion
    }
}
