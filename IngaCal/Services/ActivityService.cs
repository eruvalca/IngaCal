using System.Data;
using IngaCal.Data;
using Microsoft.EntityFrameworkCore;

namespace IngaCal.Services;

public sealed class ActivityService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser) : IActivityService
{
    public async Task<IReadOnlyList<ActivityDto>> ListAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken token = default)
    {
        var userId = await currentUser.GetIdAsync();
        await using var db = await factory.CreateDbContextAsync(token);
        var from = start.UtcDateTime;
        var to = end.UtcDateTime;
        var items = await db.Activities.AsNoTracking().Include(x => x.ActivityTags).ThenInclude(x => x.Tag)
            .Where(x => x.UserId == userId && x.StartUtc < to && x.EndUtc > from)
            .OrderBy(x => x.StartUtc).ToListAsync(token);
        return items.Select(ToDto).ToList();
    }

    public async Task<ActivityDto> SaveAsync(ActivityEdit edit, CancellationToken token = default)
    {
        var userId = await currentUser.GetIdAsync();
        var title = edit.Title.Trim();
        var description = edit.Description.Trim();
        if (title.Length == 0 && description.Length == 0) throw new JournalException("Add a title or a description.");
        if (title.Length > 200 || description.Length > 4000) throw new JournalException("Use at most 200 characters for the title and 4,000 for the description.");
        if (edit.End <= edit.Start) throw new JournalException("End time must be after start time.");
        if (edit.Start.Ticks % TimeSpan.TicksPerMinute != 0 || edit.End.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new JournalException("Enter times in whole minutes.");
        await using var db = await factory.CreateDbContextAsync(token);
        // SQLite's non-deferred Serializable transaction reserves the writer before the overlap query.
        // All application writes use this path, including changes from a second circuit/tab.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var item = edit.Id.HasValue
            ? await db.Activities.Include(x => x.ActivityTags).ThenInclude(x => x.Tag)
                .SingleOrDefaultAsync(x => x.Id == edit.Id && x.UserId == userId, token)
                ?? throw new JournalException("This activity is no longer available.")
            : new Activity { UserId = userId };
        if (edit.Id.HasValue && item.Version != edit.Version) throw new JournalException("This activity changed in another tab. Close the editor and reopen it to use the latest version.");
        var start = edit.Start.UtcDateTime;
        var end = edit.End.UtcDateTime;
        if (await db.Activities.AnyAsync(x => x.UserId == userId && x.Id != item.Id && x.StartUtc < end && x.EndUtc > start, token))
            throw new JournalException("This time overlaps another activity. Choose an open time or edit the existing activity.");
        var ids = edit.TagIds.Distinct().ToArray();
        var tags = await db.Tags.Where(x => x.UserId == userId && ids.Contains(x.Id)).ToListAsync(token);
        if (tags.Count != ids.Length) throw new JournalException("One of these tags is no longer available.");
        if (tags.Any(x => x.IsArchived && !item.ActivityTags.Any(t => t.TagId == x.Id)))
            throw new JournalException("Restore archived tags before adding them to an activity.");
        item.Title = title;
        item.Description = description;
        item.StartUtc = start;
        item.EndUtc = end;
        item.Version = Guid.NewGuid();
        foreach (var removed in item.ActivityTags.Where(x => !ids.Contains(x.TagId)).ToList())
            item.ActivityTags.Remove(removed);
        foreach (var tag in tags.Where(x => item.ActivityTags.All(t => t.TagId != x.Id)))
            item.ActivityTags.Add(new ActivityTag { ActivityId = item.Id, TagId = tag.Id, Tag = tag });
        if (!edit.Id.HasValue) db.Activities.Add(item);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new JournalException("This activity changed in another tab. Reload and try again."); }
        await transaction.CommitAsync(token);
        return ToDto(item);
    }

    public async Task DeleteAsync(Guid id, Guid version, CancellationToken token = default)
    {
        var userId = await currentUser.GetIdAsync();
        await using var db = await factory.CreateDbContextAsync(token);
        var count = await db.Activities.Where(x => x.UserId == userId && x.Id == id && x.Version == version).ExecuteDeleteAsync(token);
        if (count == 0) throw new JournalException("This activity changed or was deleted. Close the editor and refresh.");
    }

    internal static ActivityDto ToDto(Activity item) => new(item.Id, item.Title, item.Description,
        new DateTimeOffset(item.StartUtc), new DateTimeOffset(item.EndUtc), item.Version,
        item.ActivityTags.Select(x => TagService.ToDto(x.Tag)).OrderBy(x => x.Name).ToList());
}
