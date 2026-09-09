using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Jellyfin.Plugin.BetaSeries.Configuration;
using Xunit;

namespace Jellyfin.Plugin.BetaSeries.Tests;

public class ConfigurationTests
{
    [Fact]
    public void PluginConfiguration_DefaultValues_AreCorrect()
    {
        var config = new PluginConfiguration();

        Assert.Equal(string.Empty, config.ClientId);
        Assert.Equal(string.Empty, config.ClientSecret);
        Assert.Equal(string.Empty, config.RedirectUri);
        Assert.NotNull(config.Profiles);
        Assert.Empty(config.Profiles);
    }

    [Fact]
    public void BetaSeriesProfile_DefaultValues_AreCorrect()
    {
        var profile = new BetaSeriesProfile();

        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal("Nouveau profil", profile.Name);
        Assert.NotNull(profile.JellyfinUserIds);
        Assert.Empty(profile.JellyfinUserIds);
        Assert.Equal(string.Empty, profile.Token);
        Assert.Equal(string.Empty, profile.BetaSeriesUsername);
        Assert.Null(profile.BetaSeriesUserId);
        Assert.True(profile.ScrobblePlaybackStop);
        Assert.True(profile.SyncUserDataSaved);
        Assert.True(profile.SyncMovies);
        Assert.True(profile.SyncShows);
    }

    [Fact]
    public void PluginConfiguration_XmlSerializationRoundTrip_PreservesAllData()
    {
        var original = new PluginConfiguration
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            RedirectUri = "https://jellyfin.example.com/web/",
            Profiles = new[]
            {
                new BetaSeriesProfile
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Profil Alice",
                    JellyfinUserIds = new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") },
                    Token = "token-alice",
                    BetaSeriesUsername = "AliceBS",
                    BetaSeriesUserId = 12345,
                    ScrobblePlaybackStop = true,
                    SyncUserDataSaved = false,
                    SyncMovies = true,
                    SyncShows = false,
                },
                new BetaSeriesProfile
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    Name = "Profil Bob",
                    JellyfinUserIds = new[]
                    {
                        Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    },
                    Token = "token-bob",
                    BetaSeriesUsername = "BobBS",
                    BetaSeriesUserId = 67890,
                    ScrobblePlaybackStop = false,
                    SyncUserDataSaved = true,
                    SyncMovies = false,
                    SyncShows = true,
                },
            },
        };

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var stringWriter = new StringWriter();
        serializer.Serialize(stringWriter, original);

        var xml = stringWriter.ToString();
        Assert.Contains("<ClientId>test-client-id</ClientId>", xml);
        Assert.Contains("<ClientSecret>test-client-secret</ClientSecret>", xml);
        Assert.Contains("<Name>Profil Alice</Name>", xml);
        Assert.Contains("<Name>Profil Bob</Name>", xml);

        using var stringReader = new StringReader(xml);
        var deserialized = serializer.Deserialize(stringReader) as PluginConfiguration;

        Assert.NotNull(deserialized);
        Assert.Equal(original.ClientId, deserialized.ClientId);
        Assert.Equal(original.ClientSecret, deserialized.ClientSecret);
        Assert.Equal(original.RedirectUri, deserialized.RedirectUri);
        Assert.Equal(2, deserialized.Profiles.Length);

        var alice = deserialized.Profiles[0];
        Assert.Equal("Profil Alice", alice.Name);
        Assert.Equal("token-alice", alice.Token);
        Assert.Equal("AliceBS", alice.BetaSeriesUsername);
        Assert.Equal(12345, alice.BetaSeriesUserId);
        Assert.True(alice.ScrobblePlaybackStop);
        Assert.False(alice.SyncUserDataSaved);
        Assert.True(alice.SyncMovies);
        Assert.False(alice.SyncShows);
        Assert.Single(alice.JellyfinUserIds);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), alice.JellyfinUserIds[0]);

        var bob = deserialized.Profiles[1];
        Assert.Equal("Profil Bob", bob.Name);
        Assert.Equal("token-bob", bob.Token);
        Assert.Equal(2, bob.JellyfinUserIds.Length);
    }

    [Fact]
    public void Profile_Matching_FiltersCorrectlyByUserAndOptions()
    {
        var userAlice = Guid.NewGuid();
        var userBob = Guid.NewGuid();
        var userCharlie = Guid.NewGuid();

        var config = new PluginConfiguration
        {
            Profiles = new[]
            {
                new BetaSeriesProfile
                {
                    Name = "Alice Profile",
                    Token = "valid-token-alice",
                    JellyfinUserIds = new[] { userAlice },
                    ScrobblePlaybackStop = true,
                    SyncMovies = true,
                    SyncShows = false,
                },
                new BetaSeriesProfile
                {
                    Name = "Bob Profile",
                    Token = "valid-token-bob",
                    JellyfinUserIds = new[] { userBob },
                    ScrobblePlaybackStop = false,
                    SyncMovies = false,
                    SyncShows = true,
                },
                new BetaSeriesProfile
                {
                    Name = "No-token Profile",
                    Token = string.Empty,
                    JellyfinUserIds = new[] { userAlice, userBob },
                    ScrobblePlaybackStop = true,
                },
            },
        };

        // Match Alice for ScrobblePlaybackStop: only Alice Profile (No-token Profile excluded because token is empty)
        var alicePlaybackProfiles = config.Profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Token) && p.JellyfinUserIds.Contains(userAlice) && p.ScrobblePlaybackStop)
            .ToList();

        Assert.Single(alicePlaybackProfiles);
        Assert.Equal("Alice Profile", alicePlaybackProfiles[0].Name);

        // Match Bob for ScrobblePlaybackStop: Bob has ScrobblePlaybackStop = false
        var bobPlaybackProfiles = config.Profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Token) && p.JellyfinUserIds.Contains(userBob) && p.ScrobblePlaybackStop)
            .ToList();

        Assert.Empty(bobPlaybackProfiles);

        // Match Charlie: not linked to any profile
        var charlieProfiles = config.Profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Token) && p.JellyfinUserIds.Contains(userCharlie))
            .ToList();

        Assert.Empty(charlieProfiles);
    }

    [Fact]
    public void Plugin_GetPages_IncludesMainMenuConfigurationPage()
    {
        var appPathsMock = new Moq.Mock<MediaBrowser.Common.Configuration.IApplicationPaths>();
        appPathsMock.Setup(a => a.PluginsPath).Returns("/tmp");
        var xmlSerializerMock = new Moq.Mock<MediaBrowser.Model.Serialization.IXmlSerializer>();
        var plugin = new Plugin(appPathsMock.Object, xmlSerializerMock.Object);

        var pages = plugin.GetPages().ToList();

        Assert.Single(pages);
        var page = pages[0];
        Assert.Equal("BetaSeries", page.Name);
        Assert.Equal("BetaSeries", page.DisplayName);
        Assert.True(page.EnableInMainMenu);
        Assert.Contains("Configuration.configPage.html", page.EmbeddedResourcePath, StringComparison.Ordinal);
    }
}
