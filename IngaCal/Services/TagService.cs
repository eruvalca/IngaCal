using System.Text.RegularExpressions;
using IngaCal.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IngaCal.Services;

public sealed partial class TagService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser) : ITagService
{
    public async Task<IReadOnlyList<TagDto>> ListAsync(CancellationToken token = default)
    {
        var userId = await currentUser.GetIdAsync();
        await using var db = await factory.CreateDbContextAsync(token);
        return (await db.Tags.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.Name).ToListAsync(token)).Select(ToDto).ToList();
    }

    public async Task<TagDto> SaveAsync(TagEdit edit, CancellationToken token = default)
    {
        var userId = await currentUser.GetIdAsync();
        var name = edit.Name.Trim();
        if (name.Length is 0 or > 60) throw new JournalException("Tag names must have between 1 and 60 characters.");
        if (!HexColor().IsMatch(edit.Color)) throw new JournalException("Choose a valid tag color.");
        await using var db = await factory.CreateDbContextAsync(token);
        var tag = edit.Id.HasValue ? await db.Tags.SingleOrDefaultAsync(x => x.Id == edit.Id && x.UserId == userId, token)
            ?? throw new JournalException("This tag is no longer available.") : new Tag { UserId = userId };
        if (edit.Id.HasValue && tag.Version != edit.Version) throw new JournalException("This tag changed in another tab. Refresh and try again.");
        tag.Name = name;
        tag.NormalizedName = name.ToUpperInvariant();
        tag.Color = edit.Color;
        tag.IsArchived = edit.IsArchived;
        tag.Version = Guid.NewGuid();
        if (!edit.Id.HasValue) db.Tags.Add(tag);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new JournalException("This tag changed in another tab. Refresh and try again."); }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
        { throw new JournalException("You already have a tag with this name, possibly in your archived tags."); }
        return ToDto(tag);
    }

    internal static TagDto ToDto(Tag tag) => new(tag.Id, tag.Name, tag.Color, tag.IsArchived, tag.Version);
    [GeneratedRegex("^#[0-9a-fA-F]{6}\\z")]
    private static partial Regex HexColor();
}
