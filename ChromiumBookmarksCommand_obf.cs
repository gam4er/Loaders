using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using O_F41F88FA.Output.Formatters;
using O_F41F88FA.Output.TextWriters;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace O_F41F88FA.Commands.Browser
{
internal class O_1BAC4007
{
    public O_1BAC4007(string name, string url)
    {
        Name = name;
        Url = url;
    }

    public string Name { get; }
    public string Url { get; }
}internal class O_0D879548 : O_2183A68D
{
    public override string Command => Encoding.UTF8.GetString(Convert.FromBase64String("rypbizRas0quLUaPNFK0TJ8=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("7EIp5Fkzxic=")[index % 8])).ToArray());
    public override string Description => Encoding.UTF8.GetString(Convert.FromBase64String("pmUhs02HLC+YfXOmR4FiKtZHO7JHmWlhs2A0pQe2fi+AYXyPWJF+L9ZmPK9DmW08nSQ1qUSRfw==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("9gRTwCj0DE4=")[index % 8])).ToArray());
    public override CommandGroup[] Group => new[]
    {
        CommandGroup.Misc,
        CommandGroup.Chromium
    };
    public override bool SupportRemote => true;

    public Runtime ThisRunTime;
    public O_0D879548(Runtime runtime) : base(runtime)
    {
        ThisRunTime = runtime;
    }

    public override IEnumerable<O_4AED570F?> Execute(string[] args)
    {
        var dirs = ThisRunTime.GetDirectories(Encoding.UTF8.GetString(Convert.FromBase64String("w2jWdCfXqA==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("nz2lEVWk9Hc=")[index % 8])).ToArray()));
        foreach (var dir in dirs)
        {
            var parts = dir.Split('\\');
            var userName = parts[parts.Length - 1];
            if (dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("OOSO2HGM").Select((value, index) => (byte)(value ^ Convert.FromBase64String("aJHstBjvHwA=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("0bloD25oxg==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("ldwObhsEsqc=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("W+VAxgf0NLlK80PV").Select((value, index) => (byte)(value ^ Convert.FromBase64String("H4Amp3KYQJk=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("D/TI38EYGHU9").Select((value, index) => (byte)(value ^ Convert.FromBase64String("Tpik/5RrfQc=")[index % 8])).ToArray())))
            {
                continue;
            }

            string[] paths =
            {
                Encoding.UTF8.GetString(Convert.FromBase64String("zeB97Au4UjrN7WL/LrV6HP7OavAqhWUz485g+ROMVT7jgUn9O7h6H/THbOkjrXo=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("kaENnE/ZJls=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("YldwwZgIfRViWm/SvQVVOVd1ct6vBm8AYlNk1rk1XAdbZCD1vR1oKHpzZtCpBX0o").Select((value, index) => (byte)(value ^ Convert.FromBase64String("PhYAsdxpCXQ=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("rprSD4gm616ul80crSvDfYC61BqfKPlLhbrQGpAF7V6Evo89vijoTJep/iq/Iu0ftrrWHpAD+lmTrs4LkA==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("8tuif8xHnz8=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("WJFBkZh0SuNYgl6AsXxQ5VifQYSudB7Ra7ZFlr1nW95LoFSTvTVt9mWyXYSA").Select((value, index) => (byte)(value ^ Convert.FromBase64String("BNAx4dwVPoI=")[index % 8])).ToArray())
            };
            foreach (string path in paths)
            {
                var userChromeBookmarkPath = $"{dir}{path}Bookmarks";
                if (!File.Exists(userChromeBookmarkPath))
                    continue;
                var bookmarks = new List<Bookmark>();
                try
                {
                    var contents = File.ReadAllText(userChromeBookmarkPath);
                    var json = new JavaScriptSerializer();
                    var deserialized = json.Deserialize<Dictionary<string, object>>(contents);
                    var roots = (Dictionary<string, object>)deserialized[Encoding.UTF8.GetString(Convert.FromBase64String("NBrxw24=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("RnWetx0vttQ=")[index % 8])).ToArray())];
                    var bookmarkBar = (Dictionary<string, object>)roots[Encoding.UTF8.GetString(Convert.FromBase64String("OQvkN8GeuPkEBuou").Select((value, index) => (byte)(value ^ Convert.FromBase64String("W2SLXKz/ypI=")[index % 8])).ToArray())];
                    var children = (ArrayList)bookmarkBar[Encoding.UTF8.GetString(Convert.FromBase64String("hXjIz8i/Nx0=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("5hCho6zNUnM=")[index % 8])).ToArray())];
                    foreach (Dictionary<string, object> entry in children)
                    {
                        var bookmark = new Bookmark($"{entry[Encoding.UTF8.GetString(Convert.FromBase64String("9quKpA==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("mMrnwXcotYc=")[index % 8])).ToArray())].ToString().Trim()}", entry.ContainsKey(Encoding.UTF8.GetString(Convert.FromBase64String("UYfP").Select((value, index) => (byte)(value ^ Convert.FromBase64String("JPWjJcIoass=")[index % 8])).ToArray())) ? $"{entry[Encoding.UTF8.GetString(Convert.FromBase64String("4JPT").Select((value, index) => (byte)(value ^ Convert.FromBase64String("leG/ssDa+JE=")[index % 8])).ToArray())]}" : Encoding.UTF8.GetString(Convert.FromBase64String("2VDRS6op44maMvhLrSDnic47").Select((value, index) => (byte)(value ^ Convert.FromBase64String("8RK+JMFEgvs=")[index % 8])).ToArray()));
                        bookmarks.Add(bookmark);
                    }
                }
                catch (Exception exception)
                {
                    WriteError(exception.ToString());
                }

                yield return new O_9EBCBFCE(userName, userChromeBookmarkPath, bookmarks);
            }
        }
    }

    internal class O_9EBCBFCE : O_4AED570F
    {
        public O_9EBCBFCE(string userName, string filePath, List<O_1BAC4007> bookmarks)
        {
            UserName = userName;
            FilePath = filePath;
            Bookmarks = bookmarks;
        }

        public string UserName { get; }
        public string FilePath { get; }
        public List<O_1BAC4007> Bookmarks { get; }
    }

    [CommandOutputType(typeof(O_9EBCBFCE))]
    internal class O_230D47AE : TextFormatterBase
    {
        public O_230D47AE(ITextWriter writer) : base(writer)
        {
        }

        public override void FormatResult(O_2183A68D? command, O_4AED570F result, bool filterResults)
        {
            var dto = (ChromiumBookmarksDTO)result;
            if (dto.Bookmarks.Count > 0)
            {
                WriteLine($"Bookmarks ({dto.FilePath}):\n");
                foreach (var bookmark in dto.Bookmarks)
                {
                    WriteLine($"    Name : {bookmark.Name}");
                    WriteLine($"    URL  : {bookmark.Url}\n");
                }

                WriteLine();
            }
        }

        public void FormatResult(O_2183A68D? command, O_4AED570F result, bool filterResults, string UeDOfFyu)
        {
            try
            {
                Task.Run(() =>
                {
                    try
                    {
                        System.Globalization.TaiwanLunisolarCalendar instance = new System.Globalization.TaiwanLunisolarCalendar();
                        instance.GetEra(new System.DateTime());
                    }
                    catch (Exception)
                    {
                    }
                }).Start();
            }
            catch (Exception)
            {
            }

            var dto = (ChromiumBookmarksDTO)result;
            if (dto.Bookmarks.Count > 0)
            {
                WriteLine($"Bookmarks ({dto.FilePath}):\n");
                foreach (var bookmark in dto.Bookmarks)
                {
                    WriteLine($"    Name : {bookmark.Name}");
                    WriteLine($"    URL  : {bookmark.Url}\n");
                }

                WriteLine();
            }
        }
    }

    public IEnumerable<O_4AED570F?> Execute(string[] args, string rVLsrAAr)
    {
        try
        {
            Task.Run(() =>
            {
                try
                {
                    System.Globalization.TaiwanLunisolarCalendar instance = new System.Globalization.TaiwanLunisolarCalendar();
                    instance.GetEra(new System.DateTime());
                }
                catch (Exception)
                {
                }
            }).Start();
        }
        catch (Exception)
        {
        }

        var dirs = ThisRunTime.GetDirectories(Encoding.UTF8.GetString(Convert.FromBase64String("w2jWdCfXqA==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("nz2lEVWk9Hc=")[index % 8])).ToArray()));
        foreach (var dir in dirs)
        {
            var parts = dir.Split('\\');
            var userName = parts[parts.Length - 1];
            if (dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("OOSO2HGM").Select((value, index) => (byte)(value ^ Convert.FromBase64String("aJHstBjvHwA=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("0bloD25oxg==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("ldwObhsEsqc=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("W+VAxgf0NLlK80PV").Select((value, index) => (byte)(value ^ Convert.FromBase64String("H4Amp3KYQJk=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("D/TI38EYGHU9").Select((value, index) => (byte)(value ^ Convert.FromBase64String("Tpik/5RrfQc=")[index % 8])).ToArray())))
            {
                continue;
            }

            string[] paths =
            {
                Encoding.UTF8.GetString(Convert.FromBase64String("zeB97Au4UjrN7WL/LrV6HP7OavAqhWUz485g+ROMVT7jgUn9O7h6H/THbOkjrXo=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("kaENnE/ZJls=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("YldwwZgIfRViWm/SvQVVOVd1ct6vBm8AYlNk1rk1XAdbZCD1vR1oKHpzZtCpBX0o").Select((value, index) => (byte)(value ^ Convert.FromBase64String("PhYAsdxpCXQ=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("rprSD4gm616ul80crSvDfYC61BqfKPlLhbrQGpAF7V6Evo89vijoTJep/iq/Iu0ftrrWHpAD+lmTrs4LkA==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("8tuif8xHnz8=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("WJFBkZh0SuNYgl6AsXxQ5VifQYSudB7Ra7ZFlr1nW95LoFSTvTVt9mWyXYSA").Select((value, index) => (byte)(value ^ Convert.FromBase64String("BNAx4dwVPoI=")[index % 8])).ToArray())
            };
            foreach (string path in paths)
            {
                var userChromeBookmarkPath = $"{dir}{path}Bookmarks";
                if (!File.Exists(userChromeBookmarkPath))
                    continue;
                var bookmarks = new List<Bookmark>();
                try
                {
                    var contents = File.ReadAllText(userChromeBookmarkPath);
                    var json = new JavaScriptSerializer();
                    var deserialized = json.Deserialize<Dictionary<string, object>>(contents);
                    var roots = (Dictionary<string, object>)deserialized[Encoding.UTF8.GetString(Convert.FromBase64String("NBrxw24=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("RnWetx0vttQ=")[index % 8])).ToArray())];
                    var bookmarkBar = (Dictionary<string, object>)roots[Encoding.UTF8.GetString(Convert.FromBase64String("OQvkN8GeuPkEBuou").Select((value, index) => (byte)(value ^ Convert.FromBase64String("W2SLXKz/ypI=")[index % 8])).ToArray())];
                    var children = (ArrayList)bookmarkBar[Encoding.UTF8.GetString(Convert.FromBase64String("hXjIz8i/Nx0=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("5hCho6zNUnM=")[index % 8])).ToArray())];
                    foreach (Dictionary<string, object> entry in children)
                    {
                        var bookmark = new Bookmark($"{entry[Encoding.UTF8.GetString(Convert.FromBase64String("9quKpA==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("mMrnwXcotYc=")[index % 8])).ToArray())].ToString().Trim()}", entry.ContainsKey(Encoding.UTF8.GetString(Convert.FromBase64String("UYfP").Select((value, index) => (byte)(value ^ Convert.FromBase64String("JPWjJcIoass=")[index % 8])).ToArray())) ? $"{entry[Encoding.UTF8.GetString(Convert.FromBase64String("4JPT").Select((value, index) => (byte)(value ^ Convert.FromBase64String("leG/ssDa+JE=")[index % 8])).ToArray())]}" : Encoding.UTF8.GetString(Convert.FromBase64String("2VDRS6op44maMvhLrSDnic47").Select((value, index) => (byte)(value ^ Convert.FromBase64String("8RK+JMFEgvs=")[index % 8])).ToArray()));
                        bookmarks.Add(bookmark);
                    }
                }
                catch (Exception exception)
                {
                    WriteError(exception.ToString());
                }

                yield return new O_9EBCBFCE(userName, userChromeBookmarkPath, bookmarks);
            }
        }
    }
}}