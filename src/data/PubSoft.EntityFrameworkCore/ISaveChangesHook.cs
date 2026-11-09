using Microsoft.EntityFrameworkCore;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Save pipeline hook invoked before/after <see cref="DbContext.SaveChanges(bool)"/>.</summary>
public interface ISaveChangesHook
{
    /// <summary>Invoked before SaveChanges.</summary>
    void BeforeSaveChanges(DbContext context);
}