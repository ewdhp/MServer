using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace MServer.Services
{
    // This service manages FileSystemWatcher instances.
    // It creates and disposes of watchers as needed.
    // The Dispose method is called to clean up resources.
    // This is useful for monitoring file changes in a directory.
    public class FileWatcherService : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        public FileSystemWatcher CreateWatcher(string path)
        {
            var watcher = new FileSystemWatcher(path)
            {
                EnableRaisingEvents = true
            };
            _watchers.Add(watcher);
            return watcher;
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }
            _watchers.Clear();
            Console.WriteLine("All FileSystemWatcher instances disposed.");
        }
    }

    public class CleanupBackgroundService : BackgroundService
    {
        private readonly FileWatcherService _fileWatcherService;

        public CleanupBackgroundService(FileWatcherService fileWatcherService)
        {
            _fileWatcherService = fileWatcherService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Console.WriteLine("Performing cleanup...");
                _fileWatcherService.Dispose(); // Dispose of all watchers
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); // Run every 10 minutes
            }
        }
    }
}