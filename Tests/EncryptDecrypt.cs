using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tests;

public class EncryptDecrypt : BaseTest
{
    [SetUp]
    public void Setup()
    {

    }

    [Test]
    public async Task GetDestinationFileSize()
    {
        const int size = 1024 * 1024;
        using var sourceMs = await GetRandomData(size);

        var enc = new MpbkpuLib.Utility.EncryptDecrypt("password");
        using var encryptedMs = new MemoryStream();
        await enc.Encrypt(null, sourceMs, encryptedMs, CompressionLevel.NoCompression);

        var destSize = MpbkpuLib.Utility.Files.GetDestinationFileSize(size, CompressionLevel.NoCompression, true);
        if (destSize != encryptedMs.Length)
            Assert.Fail($"Expected {destSize}, got {encryptedMs.Length}");
        Assert.Pass();
    }

    [Test]
    public async Task NoCompression()
    {
        const int size = 1024 * 1024;
        using var sourceMs = await GetRandomData(size);

        var enc = new MpbkpuLib.Utility.EncryptDecrypt("password");

        using var encryptedMs = new MemoryStream();
        await enc.Encrypt(null, sourceMs, encryptedMs, CompressionLevel.NoCompression);
        encryptedMs.Seek(0, SeekOrigin.Begin);

        using var decryptedMs = new MemoryStream();
        await enc.Decrypt(encryptedMs, decryptedMs, false);
        decryptedMs.Seek(0, SeekOrigin.Begin);

        sourceMs.Seek(0, SeekOrigin.Begin);
        for (var i = 0; i < size; i++) {
            var s = sourceMs.ReadByte();
            var d = decryptedMs.ReadByte();
            if (s != d)
                Assert.Fail();
        }
        Assert.Pass();
    }

    [Test]
    public async Task Compression()
    {
        const int size = 1024 * 1024;
        using var sourceMs = await GetRandomData(size);

        var enc = new MpbkpuLib.Utility.EncryptDecrypt("password");

        using var encryptedMs = new MemoryStream();
        await enc.Encrypt(null, sourceMs, encryptedMs, CompressionLevel.SmallestSize);
        encryptedMs.Seek(0, SeekOrigin.Begin);

        using var decryptedMs = new MemoryStream();
        await enc.Decrypt(encryptedMs, decryptedMs, true);
        decryptedMs.Seek(0, SeekOrigin.Begin);

        sourceMs.Seek(0, SeekOrigin.Begin);
        for (var i = 0; i < size; i++) {
            var s = sourceMs.ReadByte();
            var d = decryptedMs.ReadByte();
            if (s != d)
                Assert.Fail();
        }
        Assert.Pass();
    }

    private async Task<MemoryStream> GetRandomData(int size)
    {
        var ms = new MemoryStream();
        var buffer = new byte[size];
        for (var i = 0; i < size; i++) {
            buffer[i] = (byte)Random.Next(0, 255);
        }

        await ms.WriteAsync(buffer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
}