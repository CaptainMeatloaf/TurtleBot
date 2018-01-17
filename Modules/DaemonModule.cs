using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Discord.Commands;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TurtleBot.Utilities;
using System.Globalization;

namespace TurtleBot.Modules
{
    [Summary("Commands that make requests to the daemon")]
    public class DaemonModule : ModuleBase<SocketCommandContext>
    {
        private HttpClient client = new HttpClient();
        private int requestid = 0;

        private async Task<JObject> SendRpcRequest(string method, string parameters = "{}")
        {
            HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, "http://us.turtlepool.space:11899/json_rpc");
            string content = $"{{ \"jsonrpc\":\"2.0\", \"method\":\"{method}\", \"params\":{parameters}, \"id\":{requestid++} }}";
            requestMessage.Content = new StringContent(content, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.SendAsync(requestMessage);

            if(response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                return JObject.Parse(responseString);
            }
            else
            {
                throw new Exception($"{(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }

        [Command("currentheight")]
        [Alias("height", "bc_height")]
        public async Task CurrentHeight([Remainder] string ignore = null)
        {
            JObject blockCountObject = await SendRpcRequest("getblockcount");
            await ReplyAsync($"The current block height is **{string.Format(CultureInfo.InvariantCulture, "{0:N0}", (long)blockCountObject["result"]["count"])}**");
        }

        [Command("currenthashrate")]
        [Alias("hashrate")]
        public async Task CurrentHashrate([Remainder] string ignore = null)
        {
            JObject lastBlockHeaderObject = await SendRpcRequest("getlastblockheader");
            await ReplyAsync($"The current global hashrate is **{HashFormatter.Format((double)lastBlockHeaderObject["result"]["block_header"]["difficulty"] / 30)}/s**");
        }

        [Command("currentsupply")]
        [Alias("supply")]
        public async Task CurrentSupply([Remainder] string ignore = null)
        {
            JObject lastBlockHeaderObject = await SendRpcRequest("getlastblockheader");
            JObject blockObject = await SendRpcRequest("f_block_json", $"{{ \"hash\":\"{lastBlockHeaderObject["result"]["block_header"]["hash"]}\"}}");
            await ReplyAsync($"The current circulating supply is **{((double)blockObject["result"]["block"]["alreadyGeneratedCoins"]/100).ToString("n2")} TRTL**");
        }

        [Command("currentdifficulty")]
        [Alias("difficulty", "diff")]
        public async Task CurrentDifficulty([Remainder] string ignore = null)
        {
            JObject lastBlockHeaderObject = await SendRpcRequest("getlastblockheader");
            await ReplyAsync($"The current difficulty is **{string.Format(CultureInfo.InvariantCulture, "{0:N0}", lastBlockHeaderObject["result"]["block_header"]["difficulty"])}**");
        }
    }
}
