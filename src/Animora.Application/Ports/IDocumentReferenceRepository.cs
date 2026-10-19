using Animora.Domain.Patients;

namespace Animora.Application.Ports;

/// <summary>
/// Metadata writes for stored files (Task 10b). The payload itself lives in the content-addressed
/// <c>ILocalFileStore</c>; this port only stages the <see cref="DocumentReference"/> row that points at it.
/// Reads come from the pet-file projection.
/// </summary>
public interface IDocumentReferenceRepository
{
    /// <summary>Stages a new document reference; the ambient unit of work commits it.</summary>
    void Add(DocumentReference entity);
}
