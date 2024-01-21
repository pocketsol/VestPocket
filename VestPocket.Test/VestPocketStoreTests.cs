using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using System.Threading;

namespace VestPocket.Test;


public class VestPocketStoreTests : IClassFixture<VestPocketStoreFixture>
{

    private VestPocketStore testStore;
    private VestPocketStoreFixture fixture;

    public VestPocketStoreTests(VestPocketStoreFixture fixture)
    {
        this.fixture = fixture;
        if (testStore != null && !testStore.IsDisposed)
        {
            testStore.Close(CancellationToken.None).Wait();
        }
        this.testStore = fixture.Get(VestPocketOptions.Default);
    }

    [Fact]
    public async Task CanCRUD()
    {
        var key = "crud";
        var entity = new Kvp(key, new TestDocument ("crud body"));
        var entitySaved = await testStore.Save(entity);

        var entityRetreived = testStore.Get<TestDocument>(key);
        Assert.Equal(entitySaved.Value, entityRetreived);

        var entityToUpdate = new Kvp(key, entityRetreived with {Body = "crud body updated"});
        var entityUpdated = await testStore.Swap(entityToUpdate, entityRetreived);

        Assert.Equal(entityToUpdate.Value, entityUpdated);

        var toDelete = new Kvp(key, null);
        await testStore.Update(toDelete, entityUpdated);
        var deletedDocument = testStore.Get<TestDocument>(key);
        Assert.Null(deletedDocument);
    }

    [Fact]
    public async Task TracksDeadSpace()
    {
        string key = "key";
        var entity1 = new TestDocument("some contents1");
        var record1 = new Kvp(key, entity1);
        await testStore.Save(record1);
        var entity2 = entity1 with { Body = "some contents2"};
        var record2 = new Kvp(key, entity2);
        await testStore.Save(record2);
        var deadSpace = testStore.DeadEntityCount;
        var deadSpaceGreaterThanZero = deadSpace > 0;
        Assert.True(deadSpaceGreaterThanZero);
    }

    [Fact] 
    public async Task CanMaintainDeadSpace()
    {
        string key = "key";
        var entity1 = new TestDocument("some contents1");
        var record1 = new Kvp(key, entity1);
        await testStore.Save(record1);
        var entity2 = entity1 with { Body = "some contents2"};
        var record2 = new Kvp(key, entity2);
        await testStore.Save(record2);
        var deadSpace = testStore.DeadEntityCount;

        var deadSpaceBeforeMaintenance = testStore.DeadEntityCount;
        await testStore.ForceMaintenance();
        var deadSpaceAfterMaintenance = testStore.DeadEntityCount;
        var deadSpaceAfterMaintenanceIsLess = deadSpaceBeforeMaintenance > deadSpaceAfterMaintenance;
        Assert.True(deadSpaceAfterMaintenanceIsLess);
    }

    [Fact]
    public async Task PrefixSearch_PartialMatch()
    {
        string key = "prefix";
        string prefix = "pre";
        string body = "body";
        var expectedDocument = await testStore.Save(new Kvp(key, new TestDocument(body)));
        var prefixResults = testStore.GetByPrefix(prefix);
        var firstMatchByPrefix = prefixResults.FirstOrDefault();
        Assert.Equal(expectedDocument.Value, firstMatchByPrefix.Value);
    }

    [Fact]
    public async Task PrefixSearch_ExactMatch()
    {
        string key = "prefix";
        string prefix = "prefix";
        string body = "body";

        var expectedDocument = await testStore.Save(new Kvp(key, new TestDocument(body)));
        var prefixResults = testStore.GetByPrefix(prefix);
        var firstMatchByPrefix = prefixResults.FirstOrDefault();
        Assert.Equal(expectedDocument.Value, firstMatchByPrefix.Value);
    }


    [Fact]
    public async Task KeysAreCaseSensitive()
    {
        var key = "crud";
        var notKey = "CrUd";

        await testStore.Save(new Kvp(key, new TestDocument("doc1")));
        await testStore.Save(new Kvp(notKey, new TestDocument("doc2")));

        var doc1 = testStore.Get<TestDocument>(key);
        var doc2 = testStore.Get<TestDocument>(notKey);

        Assert.NotEqual(doc1.Body, doc2.Body);
    }

    [Fact]
    public async Task Swap_DoesNotSwapBasedOnOldDocument()
    {
        var key = "crud";
        var doc1 = new TestDocument("version1");
        var doc2 = new TestDocument("version2");
        var doc3 = new TestDocument("version3");

        var record1 = new Kvp(key, doc1);
        await testStore.Save(record1);

        var record2 = new Kvp(key, doc2);
        var expectedDocument = await testStore.Save(record2);

        var record3 = new Kvp(key, doc3);
        var finalStoreDocument = await testStore.Swap(record3, doc1);
        Assert.Equal(record2.Value, finalStoreDocument);
    }

    [Fact]
    public async Task Save_CanProcessMultipleEntitiesAsTransaction()
    {

        var changes = new Kvp[]
        {
            new Kvp("1", new TestDocument("body1")),
            new Kvp("2", new TestDocument("body2")),
            new Kvp("3", new TestDocument("body3")),
        };

        var updated = await testStore.Save(changes);

        var expected = changes.Length;
        var actual = updated.Length;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Save_FailsWhenReadOnly()
    {
        var readonlyStore = fixture.Get(VestPocketOptions.DefaultReadOnly, false);
        var testDocument = new TestDocument("body");
        var testRecord = new Kvp("key", testDocument);
        await Assert.ThrowsAsync<Exception>(async () => await readonlyStore.Save(testRecord));
    }

    [Fact]
    public async Task Backup_CreatesBackupFile()
    {
        string filePath = "backup.db";
        if (File.Exists(filePath)) File.Delete(filePath);
        await testStore.Save(new Kvp("SomeKey", new TestDocument("SomeDoc")));
        await testStore.CreateBackup(filePath);
        var fileInfo = new FileInfo(filePath);
        var fileSize = fileInfo.Length;
        var fileNonEmpty = fileSize > 0;
        File.Delete(filePath);
        Assert.True(fileNonEmpty);
    }

    [Fact]
    public async Task Backup_ToMemory()
    {
        await testStore.Save(new Kvp("SomeKey", new TestDocument("SomeDoc")));
        using var backupMem = new MemoryStream();
        await testStore.CreateBackup(backupMem);
        var memLength = backupMem.Length;
        var memNotEmpty = memLength > 0;
        backupMem.Position = 0;
        var asUtf8 = System.Text.Encoding.UTF8.GetString(backupMem.ToArray());
        Assert.True(memNotEmpty);
    }

    [Fact]
    public async Task Backup_CanReadBackupEntities()
    {
        string filePath = "backup.db";
        if (File.Exists(filePath)) File.Delete(filePath);
        var testDocument = new TestDocument("SomeDoc");
        var testRecord = new Kvp("SomeKey", testDocument);
        await testStore.Save(testRecord);
        await testStore.CreateBackup(filePath);
        var options = new VestPocketOptions { FilePath = filePath, JsonSerializerContext = SourceGenerationContext.Default };
        options.AddType<TestDocument>();
        using var backupStore = new VestPocketStore(options);
        await backupStore.OpenAsync(CancellationToken.None);
        var testDocumentRetrieved = backupStore.Get("SomeKey");
        await backupStore.Close(CancellationToken.None);
        File.Delete(filePath);
        Assert.Equal(testRecord, testDocumentRetrieved);
    }

}
