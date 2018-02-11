using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using TurtleBot.Services;

namespace TurtleBot.Modules
{
    public class TagModule : ModuleBase<SocketCommandContext>
    {
        private readonly IConfiguration config;
        private readonly TagService tagService;

        public TagModule(IConfiguration config, TagService tagService)
        {
            this.config = config;
            this.tagService = tagService;
        }

        [Command("addtag")]
        public async Task AddTag(string tagName = "", [Remainder] string content = "")
        {
            if (UserCanEditTags())
            {
                if (String.IsNullOrWhiteSpace(tagName))
                {
                    await ReplyAsync("You must specify a name for the tag!");
                    return;
                }

                if (String.IsNullOrWhiteSpace(content))
                {
                    await ReplyAsync("You must specify content for the tag!");
                    return;
                }

                await ReplyAsync(tagService.AddTag(tagName, content, Context.User));
            }
        }

        [Command("updatetag")]
        public async Task UpdateTag(string tagName = "", [Remainder] string newContent = "")
        {
            if (UserCanEditTags())
            {
                if (String.IsNullOrWhiteSpace(tagName))
                {
                    await ReplyAsync("You must specify a name for the tag to update!");
                    return;
                }

                if (String.IsNullOrWhiteSpace(newContent))
                {
                    await ReplyAsync("You must specify the new content for the tag!");
                    return;
                }

                await ReplyAsync(tagService.UpdateTag(tagName, newContent, Context.User));
            }
        }

        [Command("deletetag")]
        public async Task DeleteTag(string tagName = "", [Remainder] string ignored = "")
        {
            if (UserCanEditTags())
            {
                if (String.IsNullOrWhiteSpace(tagName))
                {
                    await ReplyAsync("You must specify a name for the tag to update!");
                    return;
                }

                await ReplyAsync(tagService.DeleteTag(tagName, Context.User));
            }
        }

        [Command("gettaglist")]
        [Alias("tags", "taglist")]
        public async Task GetTagList([Remainder] string ignored = "")
        {
            if (UserCanUseTags())
            {
                await ReplyAsync(tagService.GetTagList());
            }
        }

        [Command("gettag")]
        [Alias("tag")]
        public async Task GetTag(string tagName, [Remainder] string ignored = "")
        {
            if (UserCanUseTags())
            {
                await ReplyAsync(tagService.GetTag(tagName));
            }
        }

        private bool UserCanEditTags()
        {
            IEnumerable<KeyValuePair<string, string>> approvedUsers = config.GetSection("tags:edit:approvedUsers").AsEnumerable();
            bool hasApprovedRole = !Context.IsPrivate && ((SocketGuildUser)Context.User).Roles.Any(y => config.GetSection("tags:edit:approvedRoles").AsEnumerable().Any(x => x.Value == y.Id.ToString()));

            return hasApprovedRole || approvedUsers.Any(x => x.Value == Context.User.Id.ToString());
        }

        private bool UserCanUseTags()
        {
            IEnumerable<KeyValuePair<string, string>> approvedUsers = config.GetSection("tags:use:approvedUsers").AsEnumerable();
            bool hasApprovedRole = !Context.IsPrivate && ((SocketGuildUser)Context.User).Roles.Any(y => config.GetSection("tags:use:approvedRoles").AsEnumerable().Any(x => x.Value == y.Id.ToString()));

            return hasApprovedRole || approvedUsers.Any(x => x.Value == Context.User.Id.ToString());
        }
    }
}
