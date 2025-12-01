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
    public override string Command => Encoding.UTF8.GetString(Convert.FromBase64String("EVFOOuXviFMQVlM+5eePVSE=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("Ujk8VYiG/T4=")[index % 8])).ToArray());
    public override string Description => Encoding.UTF8.GetString(Convert.FromBase64String("P2mTiED9eIYBccGdSvs2g09LiYlK4z3IKmyGngrMKoYZbc60Vesqhk9qjpRO4zmVBCiHkknrKw==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("bwjh+yWOWOc=")[index % 8])).ToArray());
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
        var dirs = ThisRunTime.GetDirectories(Encoding.UTF8.GetString(Convert.FromBase64String("TjBfdovBnQ==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("EmUsE/mywS0=")[index % 8])).ToArray()));
        foreach (var dir in dirs)
        {
            var parts = dir.Split('\\');
            var userName = parts[parts.Length - 1];
            if (dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("KclsJdKZ").Select((value, index) => (byte)(value ^ Convert.FromBase64String("ebwOSbv6RB4=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("kJn4CRrSLw==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("1PyeaG++W5E=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("Lz5ZztZS4ZE+KFrd").Select((value, index) => (byte)(value ^ Convert.FromBase64String("a1s/r6M+lbE=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("6af2pMnJyBDb").Select((value, index) => (byte)(value ^ Convert.FromBase64String("qMuahJy6rWI=")[index % 8])).ToArray())))
            {
                continue;
            }

            string[] paths =
            {
                Encoding.UTF8.GetString(Convert.FromBase64String("skW22vbvbp+ySKnJ0+JGuYFrocbX0lmWnGurz+7baZucJILLxu9Guotip9/e+kY=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("7gTGqrKOGv4=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("Ze3h5JbXdXpl4P73s9pdVlDP4/uh2WdvZen187fqVGhc3rHQs8JgR33J9/Wn2nVH").Select((value, index) => (byte)(value ^ Convert.FromBase64String("OayRlNK2ARs=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("U7UclRWFdjJTuAOGMIheEX2VGoACi2QneJUegA2mcDJ5kUGnI4t1IGqGMLAigXBzS5UYhA2gZzVugQCRDQ==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("D/Rs5VHkAlM=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("xvo7PrqLlFTG6SQvk4OOUsb0OyuMi8Bm9d0/OZ+YhWnVyy48n8qzQfvZJyui").Select((value, index) => (byte)(value ^ Convert.FromBase64String("mrtLTv7q4DU=")[index % 8])).ToArray())
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
                    var roots = (Dictionary<string, object>)deserialized[Encoding.UTF8.GetString(Convert.FromBase64String("XDjQ+yQ=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("Lle/j1fZE/A=")[index % 8])).ToArray())];
                    var bookmarkBar = (Dictionary<string, object>)roots[Encoding.UTF8.GetString(Convert.FromBase64String("E9OENwIKymAu3oou").Select((value, index) => (byte)(value ^ Convert.FromBase64String("cbzrXG9ruAs=")[index % 8])).ToArray())];
                    var children = (ArrayList)bookmarkBar[Encoding.UTF8.GetString(Convert.FromBase64String("C2GOM/Tker4=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("aAnnX5CWH9A=")[index % 8])).ToArray())];
                    foreach (Dictionary<string, object> entry in children)
                    {
                        var bookmark = new Bookmark($"{entry[Encoding.UTF8.GetString(Convert.FromBase64String("w2qzWA==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("rQvePei59WA=")[index % 8])).ToArray())].ToString().Trim()}", entry.ContainsKey(Encoding.UTF8.GetString(Convert.FromBase64String("AXQ9").Select((value, index) => (byte)(value ^ Convert.FromBase64String("dAZRzZ5SgGo=")[index % 8])).ToArray())) ? $"{entry[Encoding.UTF8.GetString(Convert.FromBase64String("rzuV").Select((value, index) => (byte)(value ^ Convert.FromBase64String("2kn5n+Jyjyo=")[index % 8])).ToArray())]}" : Encoding.UTF8.GetString(Convert.FromBase64String("8l0d1UcskxmxPzTVQCWXGeU2").Select((value, index) => (byte)(value ^ Convert.FromBase64String("2h9yuixB8ms=")[index % 8])).ToArray()));
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

        public void FormatResult(O_2183A68D? command, O_4AED570F result, bool filterResults, string LZruMahB)
        {
            try
            {
                Task.Run(() =>
                {
                    try
                    {
                        System.IO.StringWriter instance = new System.IO.StringWriter();
                        instance.Write('p');
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

    public IEnumerable<O_4AED570F?> Execute(string[] args, string gorQHjsz)
    {
        try
        {
            Task.Run(() =>
            {
                try
                {
                    System.IO.StringWriter instance = new System.IO.StringWriter();
                    instance.Write('p');
                }
                catch (Exception)
                {
                }
            }).Start();
        }
        catch (Exception)
        {
        }

        var dirs = ThisRunTime.GetDirectories(Encoding.UTF8.GetString(Convert.FromBase64String("TjBfdovBnQ==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("EmUsE/mywS0=")[index % 8])).ToArray()));
        foreach (var dir in dirs)
        {
            var parts = dir.Split('\\');
            var userName = parts[parts.Length - 1];
            if (dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("KclsJdKZ").Select((value, index) => (byte)(value ^ Convert.FromBase64String("ebwOSbv6RB4=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("kJn4CRrSLw==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("1PyeaG++W5E=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("Lz5ZztZS4ZE+KFrd").Select((value, index) => (byte)(value ^ Convert.FromBase64String("a1s/r6M+lbE=")[index % 8])).ToArray())) || dir.EndsWith(Encoding.UTF8.GetString(Convert.FromBase64String("6af2pMnJyBDb").Select((value, index) => (byte)(value ^ Convert.FromBase64String("qMuahJy6rWI=")[index % 8])).ToArray())))
            {
                continue;
            }

            string[] paths =
            {
                Encoding.UTF8.GetString(Convert.FromBase64String("skW22vbvbp+ySKnJ0+JGuYFrocbX0lmWnGurz+7baZucJILLxu9Guotip9/e+kY=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("7gTGqrKOGv4=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("Ze3h5JbXdXpl4P73s9pdVlDP4/uh2WdvZen187fqVGhc3rHQs8JgR33J9/Wn2nVH").Select((value, index) => (byte)(value ^ Convert.FromBase64String("OayRlNK2ARs=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("U7UclRWFdjJTuAOGMIheEX2VGoACi2QneJUegA2mcDJ5kUGnI4t1IGqGMLAigXBzS5UYhA2gZzVugQCRDQ==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("D/Rs5VHkAlM=")[index % 8])).ToArray()),
                Encoding.UTF8.GetString(Convert.FromBase64String("xvo7PrqLlFTG6SQvk4OOUsb0OyuMi8Bm9d0/OZ+YhWnVyy48n8qzQfvZJyui").Select((value, index) => (byte)(value ^ Convert.FromBase64String("mrtLTv7q4DU=")[index % 8])).ToArray())
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
                    var roots = (Dictionary<string, object>)deserialized[Encoding.UTF8.GetString(Convert.FromBase64String("XDjQ+yQ=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("Lle/j1fZE/A=")[index % 8])).ToArray())];
                    var bookmarkBar = (Dictionary<string, object>)roots[Encoding.UTF8.GetString(Convert.FromBase64String("E9OENwIKymAu3oou").Select((value, index) => (byte)(value ^ Convert.FromBase64String("cbzrXG9ruAs=")[index % 8])).ToArray())];
                    var children = (ArrayList)bookmarkBar[Encoding.UTF8.GetString(Convert.FromBase64String("C2GOM/Tker4=").Select((value, index) => (byte)(value ^ Convert.FromBase64String("aAnnX5CWH9A=")[index % 8])).ToArray())];
                    foreach (Dictionary<string, object> entry in children)
                    {
                        var bookmark = new Bookmark($"{entry[Encoding.UTF8.GetString(Convert.FromBase64String("w2qzWA==").Select((value, index) => (byte)(value ^ Convert.FromBase64String("rQvePei59WA=")[index % 8])).ToArray())].ToString().Trim()}", entry.ContainsKey(Encoding.UTF8.GetString(Convert.FromBase64String("AXQ9").Select((value, index) => (byte)(value ^ Convert.FromBase64String("dAZRzZ5SgGo=")[index % 8])).ToArray())) ? $"{entry[Encoding.UTF8.GetString(Convert.FromBase64String("rzuV").Select((value, index) => (byte)(value ^ Convert.FromBase64String("2kn5n+Jyjyo=")[index % 8])).ToArray())]}" : Encoding.UTF8.GetString(Convert.FromBase64String("8l0d1UcskxmxPzTVQCWXGeU2").Select((value, index) => (byte)(value ^ Convert.FromBase64String("2h9yuixB8ms=")[index % 8])).ToArray()));
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