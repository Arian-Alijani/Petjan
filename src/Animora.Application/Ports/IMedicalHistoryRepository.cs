using Animora.Domain.Clinical;

namespace Animora.Application.Ports;

public interface IMedicalHistoryRepository
{
    void Add(MedicalHistoryEntry entry);
}
