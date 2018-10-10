using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Core;

namespace TurtleBot.Services
{
    public class StarService
    {
        private ILogger logger;
        private readonly IConfiguration config;
        private readonly DatabaseService databaseService;
        private readonly DiscordSocketClient client;
        public Boolean IsLocked = false;

        public StarService(ILoggerFactory loggerFactory, IConfiguration config, DatabaseService databaseService, DiscordSocketClient client)
        {
            this.logger = loggerFactory.CreateLogger("starService");
            this.config = config;
            this.databaseService = databaseService;
            this.client = client;

            this.client.ReactionAdded += ClientOnReactionAdded;
            this.client.ReactionRemoved += ClientOnReactionRemoved;
        }

        public async Task LockStarboard(IUser lockerUser)
        {
            this.IsLocked = true;
            logger.LogInformation($"User {lockerUser.Username}#{lockerUser.Discriminator} ({lockerUser.Id}) has locked the starboard");
            SocketTextChannel starChannel = (SocketTextChannel)client.GetChannel(Convert.ToUInt64(config["stars:starboardChannelId"]));
            await starChannel.SendMessageAsync("The starboard has been locked 🔒");
        }

        public async Task UnlockStarboard(IUser unlockerUser)
        {
            this.IsLocked = false;
            logger.LogInformation($"User {unlockerUser.Username}#{unlockerUser.Discriminator} ({unlockerUser.Id}) has unlocked the starboard");
            SocketTextChannel starChannel = (SocketTextChannel)client.GetChannel(Convert.ToUInt64(config["stars:starboardChannelId"]));
            await starChannel.SendMessageAsync("The starboard has been unlocked 🔓");
        }

        public string BanUser(string bannedUserId, IUser bannerUser)
        {
            return $"Banning of user **{bannedUserId}** finished with status: **{ databaseService.InsertStarBan(bannedUserId, bannerUser) }**";
        }

        public string UnbanUser(string bannedUserId, IUser unbannerUser)
        {
            return $"Unbanning of user **{bannedUserId}** finished with status: **{ databaseService.DeleteStarBan(bannedUserId, unbannerUser) }**";
        }

        private async Task ClientOnReactionAdded(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel channel, SocketReaction reaction)
        {
            // Check if the starboard is locked
            if (IsLocked)
            {
                return;
            }

            IUserMessage reactedMessage = await cachedMessage.GetOrDownloadAsync();
            SocketGuildUser user = ((SocketTextChannel)channel).Guild.GetUser(reaction.UserId);

            if (reaction.Emote.Name == "⭐" && reaction.UserId != client.CurrentUser.Id && channel is SocketTextChannel)
            {
                List<DatabaseService.StarredMessageRow> starredMessageEntries;
                IChannel starChannel = client.GetChannel(Convert.ToUInt64(config["stars:starboardChannelId"]));

                // Check if the user is banned from the starboard
                if (databaseService.GetStarBan(reaction.UserId.ToString()).Any())
                {
                    await (await user.GetOrCreateDMChannelAsync()).SendMessageAsync("You are banned from participating in the starboard.");
                    return;
                }

                // Insert the star entry into the database, also accounting for there being reactions on the starboard post
                string messageIdToInsert;
                string channelIdOfStarredMessage;
                if (channel.Id == starChannel.Id) // Check if the user is trying to star the message form the starboard channel
                {
                    starredMessageEntries = databaseService.GetStarboardMessageEntry(reactedMessage.Id.ToString(), false).ToList();
                    switch (starredMessageEntries.Count)
                    {
                        case 0: // The message is not in the database, delete it from the channel
                            logger.LogError("User tried to react to message on the starboard that was not in the database");
                            await reactedMessage.DeleteAsync();
                            return;
                        case 1: // Get the ID of the original post from the starboard entry and add a star entry using that
                            messageIdToInsert = starredMessageEntries[0].OriginalMessageId;
                            channelIdOfStarredMessage = starredMessageEntries[0].OriginalChannelId;
                            break;
                        default: // Somehow the post has been entered multiple times into the starboard
                            logger.LogError($"Multiple starboard entries detected for message {reactedMessage.Id}");
                            return;
                    }
                }
                else // Otherwise, we assume they are starring the actual message
                {
                    messageIdToInsert = reactedMessage.Id.ToString();
                    channelIdOfStarredMessage = channel.Id.ToString();
                }

                // Check if the channel is excluded
                if (config.GetSection("stars:excludedChannels").AsEnumerable().Any(x => x.Value == channelIdOfStarredMessage))
                {
                    return;
                }

                // Check if the user's role is excluded
                if (config.GetSection("stars:excludedRoles").AsEnumerable().Select(z => z.Value).Intersect(user.Roles.Select(y => y.Id.ToString())).Any())
                {
                    return;
                }

                // Check if user is attempting to star their own message
                IMessage message = await ((ITextChannel)client.GetChannel(Convert.ToUInt64(channelIdOfStarredMessage))).GetMessageAsync(Convert.ToUInt64(messageIdToInsert));
                if (message.Author.Id == reaction.UserId)
                {
                    await (await user.GetOrCreateDMChannelAsync()).SendMessageAsync("You appear to have attempted to star your own message, you think too highly of yourself.");
                    return;
                }

                bool insertionResult = databaseService.InsertStarEntry(user, messageIdToInsert);
                if (!insertionResult)
                {
                    await (await user.GetOrCreateDMChannelAsync()).SendMessageAsync("You appear to have already starred the message you just attempted to star. Have you starred it in another channel?");
                    return;
                }

                // Get the number of star entries the post has
                int starEntryCount = databaseService.GetStarEntries(messageIdToInsert).Count;
                if (starEntryCount < Convert.ToInt32(config["stars:minimumThreshold"]))
                {
                    // We don't post a message to the starboard unless a minimum specified in the config is reached to avoid it getting spammed up
                    return;
                }

                // Get or create the starboard entry now that it is above the threshold, and post/update it in the starboard channel
                starredMessageEntries = databaseService.GetStarboardMessageEntry(messageIdToInsert, true).ToList();
                switch (starredMessageEntries.Count)
                {
                    case 0: // Make a starboard post and then create it in the database
                        RestUserMessage sentMessage = await ((ISocketMessageChannel)starChannel).SendMessageAsync($"{ ConvertStarCountToEmoji(starEntryCount) } **{ starEntryCount }** in channel <#{ channelIdOfStarredMessage }> for message (ID { messageIdToInsert })", embed: ConvertMessageToEmbed((IUserMessage)message));
                        await sentMessage.AddReactionAsync(new Emoji("⭐"));

                        if (!databaseService.InsertStarboardMessageEntry(message.Id.ToString(), channel, message.Author.Id.ToString() ,sentMessage))
                        {
                            await sentMessage.DeleteAsync();
                            logger.LogError("Could not insert message so deleted post for it");
                        }
                        break;
                    case 1: // Get the ID of the existing starboard post and update the value
                        DatabaseService.StarredMessageRow messageRow = starredMessageEntries.First();
                        await ((IUserMessage) await ((ISocketMessageChannel) starChannel).GetMessageAsync(Convert.ToUInt64(messageRow.StarboardMessageId))).ModifyAsync(x =>
                        {
                            x.Content = $"{ConvertStarCountToEmoji(starEntryCount)} **{starEntryCount}** in channel <#{channelIdOfStarredMessage}> for message (ID {messageIdToInsert})";
                        });
                        break;
                    default: // Somehow the post has been entered multiple times into the starboard
                        logger.LogError($"Multiple starboard entries detected for message {messageIdToInsert}");
                        return;
                }
            }
        }

        private async Task ClientOnReactionRemoved(Cacheable<IUserMessage, ulong> cachedMessage, ISocketMessageChannel channel, SocketReaction reaction)
        {
            // Check if the starboard is locked
            if (IsLocked)
            {
                return;
            }

            SocketGuildUser user = ((SocketTextChannel)channel).Guild.GetUser(reaction.UserId);

            // Check if the user is banned from the starboard
            if (databaseService.GetStarBan(reaction.UserId.ToString()).Any())
            {
                await (await user.GetOrCreateDMChannelAsync()).SendMessageAsync("You are banned from participating in the starboard.");
                return;
            }

            IUserMessage reactedMessage = await cachedMessage.GetOrDownloadAsync();

            if (reaction.Emote.Name == "⭐")
            {
                List<DatabaseService.StarredMessageRow> starredMessageEntries;
                IChannel starChannel = client.GetChannel(Convert.ToUInt64(config["stars:starboardChannelId"]));

                // Delete the star entry from the database, also accounting for there being reactions on the starboard post
                string messageIdToDelete;
                string channelIdOfStarredMessage;
                if (channel.Id == starChannel.Id) // Check if the user is trying to unstar the message form the starboard channel
                {
                    starredMessageEntries = databaseService.GetStarboardMessageEntry(reactedMessage.Id.ToString(), false).ToList();
                    switch (starredMessageEntries.Count)
                    {
                        case 0: // The message is not in the database, delete it from the channel
                            logger.LogError("User tried to unreact to message on the starboard that was not in the database");
                            await reactedMessage.DeleteAsync();
                            return;
                        case 1: // Get the ID of the original post from the starboard entry and remove the star entry from that
                            messageIdToDelete = starredMessageEntries[0].OriginalMessageId;
                            channelIdOfStarredMessage = starredMessageEntries[0].OriginalChannelId;
                            break;
                        default: // Somehow the post has been entered multiple times into the starboard
                            logger.LogError($"Multiple starboard entries detected for message {reactedMessage.Id}");
                            return;
                    }
                }
                else // Otherwise, we assume they are unstarring the actual message
                {
                    messageIdToDelete = reactedMessage.Id.ToString();
                    channelIdOfStarredMessage = channel.Id.ToString();
                }

                // Check if the channel is excluded
                if (config.GetSection("stars:excludedChannels").AsEnumerable().Any(x => x.Value == channelIdOfStarredMessage))
                {
                    return;
                }

                // Check if the user's role is excluded
                if (config.GetSection("stars:excludedRoles").AsEnumerable().Select(z => z.Value).Intersect(user.Roles.Select(y => y.Id.ToString())).Any())
                {
                    return;
                }

                var entries = databaseService.GetStarEntry(reaction.UserId.ToString(), messageIdToDelete);
                if (entries.Count == 0)
                {
                    await (await user.GetOrCreateDMChannelAsync()).SendMessageAsync("You have not starred the message you just tried to unstar. Have you already unstarred it in another channel?");
                    return;
                }
                else if (entries.Count > 1)
                {
                    logger.LogError($"User { reaction.UserId } has multiple stars stored for message ID { messageIdToDelete }, deleting all");
                }

                if (!databaseService.DeleteStarEntry(reaction.UserId.ToString(), messageIdToDelete))
                {
                    await (await user.GetOrCreateDMChannelAsync()).SendMessageAsync("There was an error unstarring the message you just tried to unstar.");
                }

                // Get or delete the starboard entry depending on star count, and delete/update it in the starboard channel
                int starEntryCount = databaseService.GetStarEntries(messageIdToDelete).Count;

                starredMessageEntries = databaseService.GetStarboardMessageEntry(messageIdToDelete, true).ToList();
                switch (starredMessageEntries.Count)
                {
                    case 0: // Entry is already missing somehow, don't do anything here because it should have already been handled
                        break;
                    case 1: // Get the ID of the existing starboard post and update the value
                        DatabaseService.StarredMessageRow messageRow = starredMessageEntries.First();
                        IUserMessage message = (IUserMessage) await ((ISocketMessageChannel) starChannel).GetMessageAsync(Convert.ToUInt64(messageRow.StarboardMessageId));

                        if (starEntryCount == 0)
                        {
                            await message.DeleteAsync();
                        }
                        else
                        {
                            await message.ModifyAsync(x =>
                            {
                                x.Content = $"{ConvertStarCountToEmoji(starEntryCount)} **{starEntryCount}** in channel <#{channelIdOfStarredMessage}> for message (ID {messageIdToDelete})";
                            });
                        }
                        break;
                    default: // Somehow the post has been entered multiple times into the starboard
                        logger.LogError($"Multiple starboard entries detected for message {messageIdToDelete}");
                        return;
                }
            }
        }

        private Embed ConvertMessageToEmbed(IUserMessage message)
        {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            EmbedAuthorBuilder embedAuthorBuilder = new EmbedAuthorBuilder();
            embedAuthorBuilder.Name = message.Author.Username;
            embedAuthorBuilder.IconUrl = message.Author.GetAvatarUrl();
            embedBuilder.Author = embedAuthorBuilder;

            IEmbed imageEmbed = message.Embeds.FirstOrDefault(x => x.Type == EmbedType.Image);
            if (imageEmbed != null)
            {
                embedBuilder.ImageUrl = imageEmbed.Thumbnail?.Url;
            }

            string[] imageExtensions = { "png", "jpeg", "jpg", "gif", "webp" };
            IAttachment imageAttachment = message.Attachments.FirstOrDefault(x => imageExtensions.Any(y => x.Filename.EndsWith(y)));
            if (imageAttachment != null)
            {
                embedBuilder.ImageUrl = imageAttachment.Url;
            }

            embedBuilder.Description = message.Content;

            embedBuilder.Timestamp = message.Timestamp;

            embedBuilder.Color = Color.Green;

            return embedBuilder.Build();
        }

        private String ConvertStarCountToEmoji(int starCount)
        {
            if (starCount < 5)
            {
                return "⭐";
            }
            else if (starCount < 10)
            {
                return "🌟";
            }
            else if (starCount < 15)
            {
                return "💫";
            }
            else if (starCount < 20)
            {
                return "🌠";
            }
            else // More than 20
            {
                return "✨";
            }
        }
    }
}
