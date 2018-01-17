using System.Threading.Tasks;
using Discord.Commands;

namespace TurtleBot.Modules
{
    public class HelpModule : ModuleBase<SocketCommandContext>
    {
        [Command("help")]
        public async Task Help(string commandToGetHelpFor = null, [Remainder] string ignore = null)
        {
            if(string.IsNullOrWhiteSpace(commandToGetHelpFor))
            {
                await ReplyAsync("Available commands: `height`, `hashrate`, `supply`, `difficulty`, `help`");
            }
            else
            {
                switch(commandToGetHelpFor.ToLowerInvariant())
                {
                    case "height":
                        await ReplyAsync("`!height` Gets the current block height");
                        return;
                    case "hashrate":
                        await ReplyAsync("`!hashrate` Gets the current global hashrate");
                        return;
                    case "supply":
                        await ReplyAsync("`!supply` Gets the current circulating supply of TRTL");
                        return;
                    case "difficulty":
                        await ReplyAsync("`!difficulty` Gets the current difficulty");
                        return;
                    case "help":
                        await ReplyAsync("`!help <(optional) command>` Prints the list of commands or help for a specific command");
                        return;
                }
            }
        }
    }
}
