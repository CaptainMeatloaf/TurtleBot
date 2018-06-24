using System.Reflection;
using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace TurtleBot.Modules
{
    [Summary("Evals commands from strings in the given context")]
    public class EvalModule : ModuleBase<SocketCommandContext>
    {
        [Command("eval")]
        [RequireOwner]
        public async Task Eval([Remainder] string codeToEval)
        {
            Script<object> evalScript = CSharpScript.Create(codeToEval, ScriptOptions.Default.WithReferences(Assembly.GetExecutingAssembly()), this.GetType());
            await ReplyAsync((await evalScript.RunAsync(this)).ReturnValue.ToString());
        }
    }
}
