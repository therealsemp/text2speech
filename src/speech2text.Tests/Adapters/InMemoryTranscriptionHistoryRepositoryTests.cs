using speech2text.Adapters.History;
using speech2text.Domain;

namespace speech2text.Tests.Adapters;

public class InMemoryTranscriptionHistoryRepositoryTests
{
    [Fact]
    public void GetAll_ReturnsEntries_MostRecentFirst()
    {
        var repository = new InMemoryTranscriptionHistoryRepository();
        var first  = new TranscriptionHistoryEntry("first", DateTimeOffset.UtcNow);
        var second = new TranscriptionHistoryEntry("second", DateTimeOffset.UtcNow);

        repository.Add(first);
        repository.Add(second);

        Assert.Equal([second, first], repository.GetAll());
    }

    [Fact]
    public void Add_BeyondMaxEntries_DropsTheOldestEntry()
    {
        var repository = new InMemoryTranscriptionHistoryRepository(maxEntries: 2);
        var first  = new TranscriptionHistoryEntry("first", DateTimeOffset.UtcNow);
        var second = new TranscriptionHistoryEntry("second", DateTimeOffset.UtcNow);
        var third  = new TranscriptionHistoryEntry("third", DateTimeOffset.UtcNow);

        repository.Add(first);
        repository.Add(second);
        repository.Add(third);

        Assert.Equal([third, second], repository.GetAll());
    }

    [Fact]
    public void GetAll_WhenNothingAdded_ReturnsEmpty()
    {
        var repository = new InMemoryTranscriptionHistoryRepository();

        Assert.Empty(repository.GetAll());
    }
}
