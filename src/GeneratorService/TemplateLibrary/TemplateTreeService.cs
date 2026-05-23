using GeneratorService.Models;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateTreeService
{
    private readonly TemplateTreeRepository _repository;

    public TemplateTreeService(TemplateTreeRepository repository)
    {
        _repository = repository;
    }

    public TemplateTreeResult GetTree(string? projectId)
    {
        var effectiveProjectId = string.IsNullOrWhiteSpace(projectId)
            ? TemplateTreeRepository.DefaultProjectId
            : projectId.Trim();

        _repository.EnsureProject(effectiveProjectId, null);
        return new TemplateTreeResult(
            true,
            effectiveProjectId,
            _repository.GetProjectName(effectiveProjectId),
            _repository.GetTree(effectiveProjectId));
    }
}
