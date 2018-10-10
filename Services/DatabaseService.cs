using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
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
        private readonly object starDatabaseLock = new object(); // Because there's always going to be one idiot that spams reacts on and off, or lots of people will star a message at once

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

                String starEntriesTableCommand = "CREATE TABLE IF NOT EXISTS StarEntries (Id INTEGER PRIMARY KEY AUTOINCREMENT, StarrerUserId TEXT NOT NULL, StarredMessageId TEXT NOT NULL, StarTime DATETIME NOT NULL)";
                SqliteCommand createStarEntriesTable = new SqliteCommand(starEntriesTableCommand, db);

                String starUsersTableCommand = "CREATE TABLE IF NOT EXISTS StarBans (UserId TEXT PRIMARY KEY, BannerUserId TEXT, BanTime DATETIME)";
                SqliteCommand createStarUsersTable = new SqliteCommand(starUsersTableCommand, db);

                String starredMessagesTableCommand = "CREATE TABLE IF NOT EXISTS StarredMessages (OriginalMessageId TEXT PRIMARY KEY, OriginalChannelId TEXT NOT NULL, OriginalAuthorId TEXT NUT NULL, StarboardMessageId TEXT NOT NULL)";
                SqliteCommand createStarredMessagesTable = new SqliteCommand(starredMessagesTableCommand, db);

                try
                {
                    createTagTable.ExecuteReader();
                    createTagRevisionsTable.ExecuteReader();
                    createStarEntriesTable.ExecuteReader();
                    createStarUsersTable.ExecuteReader();
                    createStarredMessagesTable.ExecuteReader();
                }
                catch (SqliteException e)
                {
                    logger.LogCritical($"SQLite Exception creating the tables: {e.Message}");
                }
            }
        }

        #region Tags

        public string InsertTag(string name, string content, SocketUser user)
        {
            if (!String.IsNullOrWhiteSpace(this.GetTag(name)))
            {
                return "Tag already exists!";
            }

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
                        return "Exception when inserting tag. Please inform CodIsAFish (<@186504834201944064>) about this.";
                    }

                    logger.LogInformation($"User {user.Username}#{user.Discriminator} ({user.Id}) added tag \"{name}\" with content \"{content}\"");

                    return "Success";
                }
            }
        }

        public bool UpdateTag(string name, string newContent, SocketUser user)
        {
            if (String.IsNullOrWhiteSpace(this.GetTag(name)))
            {
                return false;
            }

            using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
            {
                db.Open();

                SqliteCommand selectTagToUpdateCommand = new SqliteCommand("SELECT Id FROM Tags WHERE Name = @name", db);
                selectTagToUpdateCommand.Parameters.Add(new SqliteParameter("@name", name));

                long tagToUpdateId;

                try
                {
                    var result = selectTagToUpdateCommand.ExecuteScalar();
                    if (result == null)
                    {
                        logger.LogError($"User {user.Username}#{user.Discriminator} ({user.Id}) attempted up update nonexistent tag {name}");
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
            if (String.IsNullOrWhiteSpace(this.GetTag(name)))
            {
                return false;
            }

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
                        logger.LogWarning($"Tag \"{name}\" gave empty or null content. Possible partial deletion, or new tag is being added.");
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

        #region Stars

        public String InsertStarBan(String bannedUserId, IUser bannerUser)
        {
            lock (starDatabaseLock)
            {
                int banCount = GetStarBan(bannedUserId).Count;
                if (banCount > 0)
                {
                    if (banCount > 1)
                    {
                        logger.LogError($"User ID { bannedUserId } has multiple bans on record!");
                    }

                    return "User was already banned";
                }

                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    SqliteCommand insertBanCommand = new SqliteCommand("INSERT INTO StarBans (UserId, BannerUserId, BanTime) VALUES (@userId, @bannerId, @banTime)", db);
                    insertBanCommand.Parameters.Add(new SqliteParameter("@userId", bannedUserId));
                    insertBanCommand.Parameters.Add(new SqliteParameter("@bannerId", bannerUser.Id));
                    insertBanCommand.Parameters.Add(new SqliteParameter("@banTime", DateTime.UtcNow.ToString("O")));

                    try
                    {
                        insertBanCommand.ExecuteNonQuery();
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite Exception inserting star ban: {e.Message}");
                        return "SQLite error inserting ban into DB";
                    }

                    logger.LogInformation($"User {bannerUser.Username}#{bannerUser.Discriminator} ({bannerUser.Id}) banned user \"{bannedUserId}\"");

                    return "Success";
                }
            }
        }

        public String DeleteStarBan(String bannedUserId, IUser unbannerUser)
        {
            lock (starDatabaseLock)
            {
                var banCount = GetStarBan(bannedUserId).Count;
                if (banCount > 1)
                {
                    logger.LogError($"User {bannedUserId} has multiple bans on record, removing all");
                }
                else if (banCount == 0)
                {
                    return "User is not banned!";
                }

                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    SqliteCommand deleteBanCommand = new SqliteCommand("DELETE FROM StarBans WHERE UserId = @userid", db);
                    deleteBanCommand.Parameters.Add(new SqliteParameter("@userid", bannedUserId));

                    try
                    {
                        deleteBanCommand.ExecuteScalar();
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite error deleting star ban: {e.Message}");
                        return "SQLite error deleting ban from DB";
                    }

                    logger.LogInformation($"User {unbannerUser.Username}#{unbannerUser.Discriminator} ({unbannerUser.Id}) unbanned user \"{bannedUserId}\"");

                    return "Success";
                }
            }
        }

        public List<StarBanRow> GetStarBan(String userId)
        {
            lock (starDatabaseLock)
            {
                // This function returns a list, contrary to what the name would suggest - this function just gets the entries for a single user.
                // However, unless something went gravely wrong elsewhere, this function should only ever return 0 or 1 items!

                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    SqliteCommand selectUserEntryForMessageCommand = new SqliteCommand("SELECT * FROM StarBans WHERE UserId = @userId", db);
                    selectUserEntryForMessageCommand.Parameters.Add(new SqliteParameter("@userId", userId.ToString()));

                    List<StarBanRow> rowsToReturn = new List<StarBanRow>();

                    try
                    {
                        var result = selectUserEntryForMessageCommand.ExecuteReader();
                        if (result.HasRows)
                        {
                            while (result.Read())
                            {
                                rowsToReturn.Add(new StarBanRow
                                {
                                    UserId = result[0].ToString(),
                                    BannerUserId = result[1].ToString(),
                                    BanTime = DateTime.Parse(result[2].ToString())
                                });
                            }
                        }
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite Exception getting star ban: {e.Message}");
                    }

                    return rowsToReturn;
                }
            }
        }

        public bool InsertStarEntry(SocketUser starrer, String starredMessageId)
        {
            lock (starDatabaseLock)
            {
                int starCount = GetStarEntry(starrer.Id.ToString(), starredMessageId).Count;
                if (starCount > 0)
                {
                    logger.LogWarning($"Attempted multi-star for user { starrer.Id } on message { starredMessageId }");

                    if (starCount > 1)
                    {
                        logger.LogError($"User { starrer.Id } has { starCount } entries entered for message { starredMessageId }");
                    }

                    return false;
                }

                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    SqliteCommand insertStarCommand = new SqliteCommand("INSERT INTO StarEntries (StarrerUserId, StarredMessageId, StarTime) VALUES (@userId, @messageId, @starTime)", db);
                    insertStarCommand.Parameters.Add(new SqliteParameter("@userId", starrer.Id));
                    insertStarCommand.Parameters.Add(new SqliteParameter("@messageId", starredMessageId));
                    insertStarCommand.Parameters.Add(new SqliteParameter("@starTime", DateTime.UtcNow.ToString("O")));

                    try
                    {
                        insertStarCommand.ExecuteNonQuery();
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite Exception inserting star entry: {e.Message}");
                        return false;
                    }

                    return true;
                }
            }
        }

        public List<StarEntryRow> GetStarEntry(String starrerId, String starredMessageId)
        {
            // This function returns a list, contrary to what the name would suggest - this function just gets the entries for a single user.
            // However, unless something went gravely wrong elsewhere, this function should only ever return 0 or 1 items!

            lock (starDatabaseLock)
            {
                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    SqliteCommand selectUserEntryForMessageCommand = new SqliteCommand("SELECT * FROM StarEntries WHERE StarrerUserId = @userId AND StarredMessageId = @messageId", db);
                    selectUserEntryForMessageCommand.Parameters.Add(new SqliteParameter("@userId", starrerId));
                    selectUserEntryForMessageCommand.Parameters.Add(new SqliteParameter("@messageId", starredMessageId));

                    List<StarEntryRow> rowsToReturn = new List<StarEntryRow>();

                    try
                    {
                        var result = selectUserEntryForMessageCommand.ExecuteReader();
                        if (result.HasRows)
                        {
                            while (result.Read())
                            {
                                rowsToReturn.Add(new StarEntryRow
                                {
                                    UserId = result[1].ToString(),
                                    MessageId = result[2].ToString(),
                                    StarTime = DateTime.Parse(result[3].ToString())
                                });
                            }
                        }
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite Exception getting star entry: {e.Message}");
                    }

                    return rowsToReturn;
                }
            }
        }

        public List<StarEntryRow> GetStarEntries(String starredMessageId)
        {
            lock (starDatabaseLock)
            {
                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    SqliteCommand selectEntriesForMessageCommand = new SqliteCommand("SELECT * FROM StarEntries WHERE StarredMessageId = @messageId", db);
                    selectEntriesForMessageCommand.Parameters.Add(new SqliteParameter("@messageId", starredMessageId));

                    List<StarEntryRow> rowsToReturn = new List<StarEntryRow>();

                    try
                    {
                        var result = selectEntriesForMessageCommand.ExecuteReader();
                        if (result.HasRows)
                        {
                            while (result.Read())
                            {
                                rowsToReturn.Add(new StarEntryRow
                                {
                                    UserId = result[1].ToString(),
                                    MessageId = result[2].ToString(),
                                    StarTime = DateTime.Parse(result[3].ToString())
                                });
                            }
                        }
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite Exception getting star entries: {e.Message}");
                    }

                    return rowsToReturn;
                }
            }
        }

        public bool DeleteStarEntry(string starrerId, String starredMessageId)
        {
            lock (starDatabaseLock)
            {
                var starCount = GetStarEntry(starrerId, starredMessageId).Count;
                if (starCount > 1)
                {
                    logger.LogError($"User { starrerId } has multiple stars stored for message ID { starredMessageId }, deleting all");
                }
                else if (starCount == 0)
                {
                    return true;
                }

                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    SqliteCommand deleteStarCommand = new SqliteCommand("DELETE FROM StarEntries WHERE StarrerUserId = @userid AND StarredMessageId = @messageid", db);
                    deleteStarCommand.Parameters.Add(new SqliteParameter("@userid", starrerId));
                    deleteStarCommand.Parameters.Add(new SqliteParameter("@messageid", starredMessageId));

                    try
                    {
                        deleteStarCommand.ExecuteScalar();
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite error deleting star entry: {e.Message}");
                        return false;
                    }

                    return true;
                }
            }
        }

        public List<StarredMessageRow> GetStarboardMessageEntry(String messageId, bool isOriginallyStarredMessage)
        {
            lock (starDatabaseLock)
            {
                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    List<StarredMessageRow> rowsToReturn = new List<StarredMessageRow>();

                    SqliteCommand selectEntriesForMessageCommand = isOriginallyStarredMessage ? new SqliteCommand("SELECT * FROM StarredMessages WHERE OriginalMessageId = @messageId", db) : new SqliteCommand("SELECT * FROM StarredMessages WHERE StarboardMessageId = @messageId", db);

                    selectEntriesForMessageCommand.Parameters.Add(new SqliteParameter("@messageId", messageId));

                    try
                    {
                        var result = selectEntriesForMessageCommand.ExecuteReader();
                        if (result.HasRows)
                        {
                            while (result.Read())
                            {
                                rowsToReturn.Add(new StarredMessageRow
                                {
                                    OriginalMessageId = result[0].ToString(),
                                    OriginalChannelId = result[1].ToString(),
                                    OriginalAuthorId = result [2].ToString(),
                                    StarboardMessageId = result[3].ToString()
                                });
                            }
                        }
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite Exception getting star entries: {e.Message}");
                    }

                    return rowsToReturn;
                }
            }
        }

        public bool InsertStarboardMessageEntry(String starredMessageId, IChannel originalChannel, String starredMessageAuthorId, IMessage starboardMessage) // Author can be gotten from message
        {
            lock (starDatabaseLock)
            {
                int messageCount = GetStarboardMessageEntry(starredMessageId, true).Count();
                if (messageCount > 0)
                {
                    logger.LogError($"Attempted multi-insertion into starboard for message { starredMessageId }");

                    if (messageCount > 1)
                    {
                        logger.LogError($"There are { messageCount } entries inserted for message { starredMessageId }");
                    }

                    return false;
                }

                using (SqliteConnection db = new SqliteConnection(config.GetSection("database")["connectionString"]))
                {
                    db.Open();

                    SqliteCommand insertStarCommand = new SqliteCommand("INSERT INTO StarredMessages (OriginalMessageId, OriginalChannelId, OriginalAuthorId, StarboardMessageId) VALUES (@originalMessageId, @originalChannelId, @originalAuthorId, @starboardMessageId)", db);
                    insertStarCommand.Parameters.Add(new SqliteParameter("@originalMessageId", starredMessageId));
                    insertStarCommand.Parameters.Add(new SqliteParameter("@originalChannelId", originalChannel.Id));
                    insertStarCommand.Parameters.Add(new SqliteParameter("@originalAuthorId", starredMessageAuthorId));
                    insertStarCommand.Parameters.Add(new SqliteParameter("@starboardMessageId", starboardMessage.Id));

                    try
                    {
                        insertStarCommand.ExecuteNonQuery();
                    }
                    catch (SqliteException e)
                    {
                        logger.LogError($"SQLite Exception inserting starboard message: {e.Message}");
                        return false;
                    }

                    return true;
                }
            }
        }

        public class StarEntryRow // CREATE TABLE IF NOT EXISTS StarEntries (Id INTEGER PRIMARY KEY AUTOINCREMENT, StarrerUserId TEXT NOT NULL, StarredMessageId TEXT NOT NULL, StarTime DATETIME NOT NULL)
        {
            public String UserId { get; set; }

            public String MessageId { get; set; }

            public DateTime StarTime { get; set; }
        }

        public class StarredMessageRow // CREATE TABLE IF NOT EXISTS StarredMessages (OriginalMessageId TEXT PRIMARY KEY, OriginalChannelId TEXT NOT NULL, OriginalAuthorId TEXT NUT NULL, StarboardMessageId TEXT NOT NULL)
        {
            public String OriginalMessageId { get; set; }

            public String OriginalChannelId { get; set; }

            public String OriginalAuthorId { get; set; }

            public String StarboardMessageId { get; set; }
        }

        public class StarBanRow // CREATE TABLE IF NOT EXISTS StarBans (UserId TEXT PRIMARY KEY, BannerUserId TEXT, BanTime DATETIME)
        {
            public String UserId { get; set; }

            public String BannerUserId { get; set; }

            public DateTime BanTime { get; set; }
        }

        #endregion
    }
}
