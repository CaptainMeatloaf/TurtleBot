using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TurtleBot.Services
{
    public class TagService
    {
        private readonly IConfiguration config;
        private readonly DatabaseService databaseService;

        public TagService(IConfiguration config, DatabaseService databaseService)
        {
            this.config = config;
            this.databaseService = databaseService;
        }

        public string AddTag(string tagName, string tagContent, SocketUser user)
        {
            return $"Adding of tag **{tagName}** finished with status: **{databaseService.InsertTag(tagName, tagContent, user)}**";
        }

        public string UpdateTag(string tagName, string newContent, SocketUser user)
        {
            if (databaseService.UpdateTag(tagName, newContent, user))
            {
                return $"Updated tag **{tagName}**";
            }
            else
            {
                return $"Failed to update tag **{tagName}**. Does the tag that you are trying to update exist?\n\nIf it does, please inform CodIsAFish (<@186504834201944064>).";
            }
        }

        public string DeleteTag(string tagName, SocketUser user)
        {
            if (databaseService.DeleteTag(tagName, user))
            {
                return $"Deleted tag **{tagName}**";
            }
            else
            {
                return $"Failed to delete tag **{tagName}**. Does the tag that you are trying to delete exist?\n\nIf it does, please inform CodIsAFish (<@186504834201944064>).";
            }
        }

        public string GetTagList()
        {
            List<String> tagList = databaseService.GetTagList();
            tagList.sort();
            if (tagList.Any())
            {
                return $"Current tag list: {String.Join(", ", tagList.Select(x => $"`{x}`"))}";
            }
            else
            {
                return "There are no tags in the database! If you think there should be, please inform CodIsAFish (<@186504834201944064>)";
            }
        }

        public string GetTag(string name)
        {
            string content = databaseService.GetTag(name);
            if (String.IsNullOrWhiteSpace(content))
            {
                return $"There is no tag with the name **{name}**";
            }
            else
            {
                return content;
            }
        }
    }
}
