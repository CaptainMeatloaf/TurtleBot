using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using TurtleBot.Services;

namespace TurtleBot.Modules
{
    public class StarModule : ModuleBase<SocketCommandContext>
    {
        private readonly IConfiguration config;
        private StarService starService;

        public StarModule(IConfiguration config, StarService starService)
        {
            this.config = config;
            this.starService = starService;
        }

        [Command("starban")]
        public async Task BanUser(string userId = null, [Remainder] string remainder = null)
        {
            if (UserCanBan())
            {
                if (userId != null && userId.All(Char.IsDigit))
                {
                    await ReplyAsync(starService.BanUser(userId, Context.User));
                }
                else if (Context.Message.MentionedUsers.Any())
                {
                    foreach (SocketUser mentionedUser in Context.Message.MentionedUsers)
                    {
                        await ReplyAsync(starService.BanUser(mentionedUser.Id.ToString(), Context.User));
                    }
                }
                else
                {
                    await ReplyAsync("You did not specify a valid user ID or tag any users you wish to ban from the starboard");
                }
            }
        }

        [Command("starunban")]
        public async Task UnbanUser(string userId = null, [Remainder] string remainder = null)
        {
            if (UserCanBan())
            {
                if (userId != null && userId.All(Char.IsDigit))
                {
                    await ReplyAsync(starService.BanUser(userId, Context.User));
                }
                else if (Context.Message.MentionedUsers.Any())
                {
                    foreach (SocketUser mentionedUser in Context.Message.MentionedUsers)
                    {
                        await ReplyAsync(starService.UnbanUser(mentionedUser.Id.ToString(), Context.User));
                    }
                }
                else
                {
                    await ReplyAsync("You did not specify a valid user ID or tag any users you wish to unban from the starboard");
                }
            }
        }

        [Command("lockstarboard")]
        public async Task LockStarboard()
        {
            if (UserCanLock())
            {
                if (starService.IsLocked)
                {
                    await (await Context.User.GetOrCreateDMChannelAsync()).SendMessageAsync("The starboard is already locked!");
                }
                else
                {
                    await starService.LockStarboard(Context.User);
                }
            }
        }

        [Command("unlockstarboard")]
        public async Task UnlockStarboard()
        {
            if (UserCanLock())
            {
                if (!starService.IsLocked)
                {
                    await (await Context.User.GetOrCreateDMChannelAsync()).SendMessageAsync("The starboard is already unlocked!");
                }
                else
                {
                    await starService.UnlockStarboard(Context.User);
                }
            }
        }

        private bool UserCanBan()
        {
            IEnumerable<KeyValuePair<string, string>> approvedUsers = config.GetSection("stars:ban:approvedUsers").AsEnumerable();
            bool hasApprovedRole = !Context.IsPrivate && ((SocketGuildUser)Context.User).Roles.Any(y => config.GetSection("stars:ban:approvedRoles").AsEnumerable().Any(x => x.Value == y.Id.ToString()));

            return hasApprovedRole || approvedUsers.Any(x => x.Value == Context.User.Id.ToString());
        }

        private bool UserCanLock()
        {
            IEnumerable<KeyValuePair<string, string>> approvedUsers = config.GetSection("stars:lock:approvedUsers").AsEnumerable();
            bool hasApprovedRole = !Context.IsPrivate && ((SocketGuildUser)Context.User).Roles.Any(y => config.GetSection("stars:lock:approvedRoles").AsEnumerable().Any(x => x.Value == y.Id.ToString()));

            return hasApprovedRole || approvedUsers.Any(x => x.Value == Context.User.Id.ToString());
        }
    }
}
