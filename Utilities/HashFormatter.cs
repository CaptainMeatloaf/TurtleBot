namespace TurtleBot.Utilities
{
    public static class HashFormatter
    {
        public static string Format(double hashrate)
        {
            var i = 0;
            string[] byteUnits = new []{" H", " KH", " MH", " GH", " TH", " PH" };
            while (hashrate > 1000){
                hashrate = hashrate / 1000;
                i++;
            }
            return hashrate.ToString("n2") + byteUnits[i];
        }
    }
}