using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace MGS4_Master_Collection_Trainer
{
    internal static class VersionSupport
    {
        private static readonly string Repo = "MGS4-Master-Collection-Trainer";
        private static readonly string Owner = "ANTIBigBoss";

        private class Tag
        {
            public string Name { get; set; }
            public int? MajorVersion { get; set; }
            public int? MinorVersion { get; set; }
            public int? BuildVersion { get; set; }
            public int? RevisionVersion { get; set; }

            public Tag(string name)
            {
                Name = name.ToLower();

                string number = Name.Contains('v')
                    ? Name.Split('v')[1]
                    : Name;

                string[] parts = number.Split('.');

                MajorVersion = int.TryParse(
                    parts[0],
                    out int majorVersion)
                    ? majorVersion
                    : (int?)null;

                if (parts.Length >= 2)
                {
                    MinorVersion = int.TryParse(
                        parts[1],
                        out int minorVersion)
                        ? minorVersion
                        : (int?)null;
                }

                if (parts.Length >= 3)
                {
                    BuildVersion = int.TryParse(
                        parts[2],
                        out int buildVersion)
                        ? buildVersion
                        : (int?)null;
                }

                if (parts.Length >= 4)
                {
                    RevisionVersion = int.TryParse(
                        parts[3],
                        out int revisionVersion)
                        ? revisionVersion
                        : (int?)null;
                }
            }
        }

        public static bool CheckIfNewUpdateExists(string appVersion)
        {
            GitHubClient gitHubClient =
                new GitHubClient(new Octokit.ProductHeaderValue(Repo));

            IReadOnlyList<Release> releases =
                gitHubClient.Repository.Release
                    .GetAll(Owner, Repo)
                    .Result;

            Tag highestTag = null;

            foreach (Release release in releases)
            {
                Tag tag = new Tag(release.TagName);

                if (highestTag == null)
                {
                    highestTag = tag;
                }

                if (highestTag != tag)
                {
                    if (CheckIfTagIsNewer(highestTag, tag))
                    {
                        highestTag = tag;
                    }
                }
            }

            if (highestTag == null)
            {
                return false;
            }

            Tag currentVersionTag =
                new Tag("v" + appVersion);

            if (string.Equals(
                currentVersionTag.Name,
                highestTag.Name))
            {
                return false;
            }

            return !CheckIfTagIsNewer(
                highestTag,
                currentVersionTag);
        }

        private static bool CheckIfTagIsNewer(
            Tag highestTag,
            Tag tagToCheck)
        {
            if (highestTag.MajorVersion <
                tagToCheck.MajorVersion)
            {
                return true;
            }

            if (highestTag.MajorVersion ==
                    tagToCheck.MajorVersion &&
                highestTag.MinorVersion <
                    tagToCheck.MinorVersion)
            {
                return true;
            }

            if (highestTag.MajorVersion ==
                    tagToCheck.MajorVersion &&
                highestTag.MinorVersion ==
                    tagToCheck.MinorVersion &&
                highestTag.BuildVersion <
                    tagToCheck.BuildVersion)
            {
                return true;
            }

            if (highestTag.MajorVersion ==
                    tagToCheck.MajorVersion &&
                highestTag.MinorVersion ==
                    tagToCheck.MinorVersion &&
                highestTag.BuildVersion ==
                    tagToCheck.BuildVersion &&
                highestTag.RevisionVersion <
                    tagToCheck.RevisionVersion)
            {
                return true;
            }

            return false;
        }

        public static void DownloadAutoUpdater()
        {
            GitHubClient gitHubClient =
                new GitHubClient(
                    new Octokit.ProductHeaderValue(Repo));

            IReadOnlyList<Release> releases =
                gitHubClient.Repository.Release
                    .GetAll(
                        "sagefantasma",
                        "AutoUpdater")
                    .Result;

            Release latestRelease = releases[0];

            ReleaseAsset desiredAsset =
                latestRelease.Assets.First(
                    x => string.Equals(
                        x.Name,
                        "AutoUpdater.exe"));

            using (HttpClient httpClient =
                   new HttpClient())
            {
                byte[] fileBytes =
                    httpClient
                        .GetAsync(
                            desiredAsset
                                .BrowserDownloadUrl)
                        .Result
                        .Content
                        .ReadAsByteArrayAsync()
                        .Result;

                string autoUpdaterLocation =
                    Path.Combine(
                        Directory
                            .GetParent(
                                Environment
                                    .CurrentDirectory)
                            .FullName,
                        "AutoUpdater.exe");

                File.WriteAllBytes(
                    autoUpdaterLocation,
                    fileBytes);
            }
        }

        public static void StartAutoUpdater()
        {
            string fileToStart =
                Path.Combine(
                    Directory
                        .GetParent(
                            Environment.CurrentDirectory)
                        .FullName,
                    "AutoUpdater.exe");

            ProcessStartInfo processStartInfo =
                new ProcessStartInfo();

            processStartInfo.FileName =
                fileToStart;

            processStartInfo.Arguments =
                "-r MGS4-Master-Collection-Trainer " +
                "-o ANTIBigBoss " +
                "-a \"MGS4 Master Collection Trainer\"";

            // Critical for Sage's updater.
            processStartInfo.WorkingDirectory =
                Environment.CurrentDirectory;

            Process.Start(processStartInfo);
        }
    }
}