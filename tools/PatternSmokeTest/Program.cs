using Jellyfin.Plugin.AutoOrganize.Core;

static void Equal(string expected, string actual, string label)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new Exception(label + ": expected " + expected + " but got " + actual);
    }
    Console.WriteLine("OK: " + label);
}

const string replaceColon = " - ";
var info = new EpisodeInfo("Укрытие", 3, 5, "Воспоминание", 2023, SourceStem: "Укрытие.S03E05", OriginalSeriesTitle: "Укрытие");
Equal("Укрытие (2023)", TemplateRenderer.Episode("%sn (%sy)", info, "", replaceColon), "series folder");
Equal("Season 3", TemplateRenderer.Episode("Season %s", info, "", replaceColon), "season unpadded");
Equal("Season 03", TemplateRenderer.Episode("Season %0s", info, "", replaceColon), "season padded");
Equal("Season 00", TemplateRenderer.Episode("Season %0s", info with { Season = 0 }, "", replaceColon), "specials default");
Equal("Укрытие - S03E05 - Воспоминание.mkv",
    TemplateRenderer.Episode("%sn - S%0sE%0e - %en.%ext", info, ".mkv", replaceColon),
    "localized TMDB episode title");
Equal("Укрытие (2023)", TemplateRenderer.Episode("{n} ({y})", info, "", replaceColon), "FileBot name and year");
Equal("Season 03", TemplateRenderer.Episode("{'Season 0'+s}", info, "", replaceColon), "FileBot season expression");
Equal("Укрытие - S03E05 - Воспоминание.mkv",
    TemplateRenderer.Episode("{n} - {s00e00} - {t}{ext}", info, ".mkv", replaceColon),
    "FileBot episode expression");
var unknown = info with { EpisodeTitle = null };
Equal("Укрытие - S03E05.mkv",
    TemplateRenderer.Episode("%sn - S%0sE%0e - %en.%ext", unknown, ".mkv", replaceColon),
    "empty title must not become underscore");
Equal("Укрытие - S03E05.mkv",
    TemplateRenderer.Episode("{n} - {s00e00} - {t}{ext}", unknown, ".mkv", replaceColon),
    "FileBot empty title cleanup");
Equal("Укрытие - S03E05.mkv",
    TemplateRenderer.Episode("{series} - S{season:00}E{episode:00} - {episodeTitle}{ext}",
        unknown, ".mkv", replaceColon),
    "legacy empty title cleanup");
var parsed = MediaFileParser.ParseEpisode("/video/Укрытие.S03E05.mkv");
if (parsed is null || parsed.Season != 3 || parsed.Episode != 5 || parsed.Series != "Укрытие")
    throw new Exception("Failed to parse a single episode");
Console.WriteLine("OK: episode parser");
var parsedMulti = MediaFileParser.ParseEpisode("/video/Укрытие.S03E05E06.mkv");
if (parsedMulti is null || parsedMulti.EndingEpisode != 6)
    throw new Exception("Failed to parse multiple episodes");
Console.WriteLine("OK: multi-episode parser");
Console.WriteLine("All naming smoke tests passed.");
