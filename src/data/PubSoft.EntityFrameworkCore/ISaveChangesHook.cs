using Microsoft.EntityFrameworkCore;

namespace PubSoft.EntityFrameworkCore;

public interface ISaveChangesHook
{
    void BeforeSaveChanges(DbContext context);
}