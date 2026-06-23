using ClientManagementSystem.Services;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Controllers;

public sealed class ProjectsController : Controller
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = await _projectService.PrepareCreateFormAsync(cancellationToken: cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProjectFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(await _projectService.PrepareCreateFormAsync(model, cancellationToken));
        }

        var projectId = await _projectService.SaveAsync(model, cancellationToken);
        TempData["SuccessMessage"] = "Project saved successfully.";
        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var model = await _projectService.PrepareEditFormAsync(id, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View("Create", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProjectFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Create", await _projectService.PrepareCreateFormAsync(model, cancellationToken));
        }

        await _projectService.SaveAsync(model, cancellationToken);
        TempData["SuccessMessage"] = "Project updated successfully.";
        return RedirectToAction(nameof(Edit), new { id = model.ProjectID });
    }
}
