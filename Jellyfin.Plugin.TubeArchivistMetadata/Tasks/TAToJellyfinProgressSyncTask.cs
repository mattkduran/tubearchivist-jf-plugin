using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TubeArchivistMetadata.TubeArchivist;
using Jellyfin.Plugin.TubeArchivistMetadata.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TubeArchivistMetadata.Tasks
{
    /// <summary>
    /// Task to sync TubeArchivist playback progresses to Jellyfin.
    /// </summary>
    public class TAToJellyfinProgressSyncTask : IScheduledTask
    {
        private readonly ILogger<Plugin> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TAToJellyfinProgressSyncTask"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        public TAToJellyfinProgressSyncTask(ILogger<Plugin> logger, ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;
        }

        /// <inheritdoc/>
        public string Name => "TAToJellyfinProgressSyncTask";

        /// <inheritdoc/>
        public string Description => "This tasks syncs TubeArchivist playback progresses to Jellyfin";

        /// <inheritdoc/>
        public string Category => "TubeArchivistMetadata";

        /// <inheritdoc/>
        public string Key => "TAToJellyfinProgressSyncTask";

        /// <inheritdoc/>
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            // TODO: Check if the TA->JF synchronization is enabled from the configuration before proceeding
            _logger.LogInformation("Starting TubeArchivist playback progresses synchronization.");
            // TODO: Replace with the lists from configuration
            var jfUsernames = new[] { "c", "c" };
            var taUsernames = new[] { "c", "c" };
            var taApi = TubeArchivistApi.GetInstance();
            foreach (var jfUsername in jfUsernames)
            {
                var user = _userManager.GetUserByName(jfUsername);
                if (user == null)
                {
                    _logger.LogInformation("{Message}", $"Jellyfin user with username {jfUsername} not found");
                    continue;
                }

                var collectionItem = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Name = Plugin.Instance?.Configuration.CollectionTitle,
                    IncludeItemTypes = new[] { BaseItemKind.CollectionFolder }
                }).FirstOrDefault();

                if (collectionItem == null)
                {
                    var message = $"Collection '{Plugin.Instance?.Configuration.CollectionTitle}' not found.";
                    _logger.LogCritical("{Message}", message);
                }
                else
                {
                    var channels = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        ParentId = collectionItem.Id,
                        IncludeItemTypes = new[] { BaseItemKind.Series }
                    });

                    foreach (var channel in channels)
                    {
                        var years = _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            ParentId = channel.Id,
                            IncludeItemTypes = new[] { BaseItemKind.Season }
                        });

                        foreach (var year in years)
                        {
                            var videos = _libraryManager.GetItemList(new InternalItemsQuery
                            {
                                ParentId = year.Id,
                                IncludeItemTypes = new[] { BaseItemKind.Episode }
                            });

                            foreach (var video in videos)
                            {
                                var playbackProgress = await taApi.GetProgress(Utils.GetVideoNameFromPath(video.Path)).ConfigureAwait(true);
                                if (playbackProgress != null)
                                {
                                    var userItemData = _userDataManager.GetUserData(user, video);
                                    var userUpdateData = new UpdateUserItemDataDto
                                    {
                                        PlaybackPositionTicks = playbackProgress.Position * TimeSpan.TicksPerSecond
                                    };

                                    // TODO: Also last played datetime should be updated once TA will return it
                                    if (userItemData.PlaybackPositionTicks >= video.RunTimeTicks)
                                    {
                                        userUpdateData.Played = true;
                                    }
                                    else
                                    {
                                        userUpdateData.Played = false;
                                    }

                                    _userDataManager.SaveUserData(user, video, userUpdateData, UserDataSaveReason.UpdateUserData);
                                    _logger.LogInformation("{Message}", $"Playback progress for video {video.Name} set to {userItemData.PlaybackPositionTicks / TimeSpan.TicksPerSecond} seconds for user {jfUsername}.");
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return
            [
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    // TODO: Use configuration defined interval
                    IntervalTicks = TimeSpan.FromSeconds(5).Ticks
                },
            ];
        }
    }
}
