using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using MServer.Services;

public class UserRequestLockMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IUCService _userContextService;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UserLocks = new();

    public UserRequestLockMiddleware(RequestDelegate next, IUCService userContextService)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _userContextService = userContextService ?? throw new ArgumentNullException(nameof(userContextService));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Guid userId;

        try
        {
            // Retrieve the user ID using the UserContextService
            userId = _userContextService.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            // If the user ID is invalid or missing, proceed without locking
            await _next(context);
            return;
        }

        // Get or create a lock for the user
        var userLock = UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

        await userLock.WaitAsync();
        try
        {
            Console.WriteLine($"Lock acquired for User ID: {userId}");
            // Process the request
            await _next(context);

        }
        finally
        {
            // Release the lock and remove it from the dictionary if no other threads are waiting
            userLock.Release();
            if (userLock.CurrentCount == 1)
            {
                UserLocks.TryRemove(userId, out _);
            }
            Console.WriteLine($"Lock released for User ID: {userId}");
        }
    }
}