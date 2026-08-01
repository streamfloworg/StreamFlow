using StreamFlow.App.Services;
using StreamFlow.Core.AudioHandling;

using Xunit;

namespace StreamFlow.Tests;

public class CategoryMediaServiceTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"category_media_test_{Guid.NewGuid():N}");
    private static readonly List<FileExtension> ImageExtensions = [new("png"), new("jpg"), new("jpeg")];

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true);
    }

    private void CreateFile(string name)
    {
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllBytes(Path.Combine(_tempFolder, name), []);
    }

    [Fact]
    public void FindExistingMedia_MatchesFilenameContainingCategory()
    {
        CreateFile("Half-Life 2 wallpaper.png");

        var result = CategoryMediaService.FindExistingMedia(_tempFolder, "Half-Life 2", ImageExtensions);

        Assert.NotNull(result);
        Assert.Equal("Half-Life 2 wallpaper.png", Path.GetFileName(result));
    }

    [Fact]
    public void FindExistingMedia_IsCaseInsensitive()
    {
        CreateFile("HALF-LIFE 2.jpg");

        var result = CategoryMediaService.FindExistingMedia(_tempFolder, "half-life 2", ImageExtensions);

        Assert.NotNull(result);
    }

    [Fact]
    public void FindExistingMedia_IgnoresNonImageExtensions()
    {
        CreateFile("Half-Life 2 notes.txt");

        var result = CategoryMediaService.FindExistingMedia(_tempFolder, "Half-Life 2", ImageExtensions);

        Assert.Null(result);
    }

    [Fact]
    public void FindExistingMedia_ReturnsNull_WhenNoFilenameMatches()
    {
        CreateFile("Portal 2.png");

        var result = CategoryMediaService.FindExistingMedia(_tempFolder, "Half-Life 2", ImageExtensions);

        Assert.Null(result);
    }

    [Fact]
    public void FindExistingMedia_ReturnsNull_WhenFolderDoesNotExist()
    {
        var result = CategoryMediaService.FindExistingMedia(Path.Combine(_tempFolder, "does-not-exist"), "Half-Life 2", ImageExtensions);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("Half-Life 2", "Half-Life 2")]
    [InlineData("Grand Theft Auto V", "Grand Theft Auto V")]
    [InlineData("Baldur's Gate 3", "Baldur's Gate 3")]
    public void SanitizeFileName_LeavesValidNamesUnchanged(string input, string expected)
    {
        Assert.Equal(expected, CategoryMediaService.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_ReplacesInvalidCharactersWithUnderscore()
    {
        var result = CategoryMediaService.SanitizeFileName("Category: The \"Best\" One / Ever?");

        Assert.DoesNotContain(':', result);
        Assert.DoesNotContain('"', result);
        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('?', result);
    }

    [Fact]
    public void SanitizeFileName_ResultIsAlwaysAValidFileNameComponent()
    {
        var sanitized = CategoryMediaService.SanitizeFileName("A:B/C\\D*E?F\"G<H>I|J");
        var path = Path.Combine(_tempFolder, $"{sanitized}.png");

        Directory.CreateDirectory(_tempFolder);
        var ex = Record.Exception(() => File.WriteAllBytes(path, []));

        Assert.Null(ex);
    }
}
