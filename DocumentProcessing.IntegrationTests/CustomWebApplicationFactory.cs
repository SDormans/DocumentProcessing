using DocumentProcessing.Application.Services;
using System.IO;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using Moq;

namespace DocumentProcessing.IntegrationTests;

public static class TestFixture
{
    public static Mock<IDocumentRepository> MockRepository { get; } = new();
    public static Mock<IFileStorage> MockFileStorage { get; } = new();

    public static void Reset()
    {
        MockRepository.Reset();
        MockFileStorage.Reset();

        // Default: repository returns null for GetByIdAsync
        MockRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        MockRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Document>() as IReadOnlyList<Document>);

        MockRepository.Setup(r => r.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) =>
            {
                // Assign an ID if not set
                if (d.Id == Guid.Empty)
                {
                    // Can't set private setter easily, but Document.Create sets it
                }
            })
            .Returns(Task.CompletedTask);

        MockRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockFileStorage.Setup(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string fileName, CancellationToken _) => $"storage/{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
    }
}