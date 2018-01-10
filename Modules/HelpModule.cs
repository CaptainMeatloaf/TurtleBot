using System.Threading.Tasks;
using Discord.Commands;

namespace TurtleBot.Modules
{
    public class HelpModule : ModuleBase<SocketCommandContext>
    {
        [Command("help")]
        public Task Help()
            => ReplyAsync(
                $"The following commands are available:\n```" +
                "!height:      Gets the current block height\n" +
                "!hashrate:    Gets the current global hashrate\n" +
                "!supply:      Gets the current circulating supply of TRTL\n" +
                "!difficulty:  Gets the current difficulty\n" + 
                "!help:        Prints this help dialog```");
    }
}
