using ClientManagementSystem.Services;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Controllers;

public sealed class ClientsController : Controller
{
    private readonly IClientService _clientService;

    public ClientsController(IClientService clientService)
    {
        _clientService = clientService;
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = await _clientService.PrepareCreateFormAsync(cancellationToken: cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ClientFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(await _clientService.PrepareCreateFormAsync(model, cancellationToken));
        }

        var clientId = await _clientService.SaveAsync(model, cancellationToken);
        TempData["SuccessMessage"] = "Client saved successfully.";
        return RedirectToAction(nameof(Edit), new { id = clientId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var model = await _clientService.PrepareEditFormAsync(id, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View("Create", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ClientFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Create", await _clientService.PrepareCreateFormAsync(model, cancellationToken));
        }

        await _clientService.SaveAsync(model, cancellationToken);
        TempData["SuccessMessage"] = "Client updated successfully.";
        return RedirectToAction(nameof(Edit), new { id = model.ClientID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        await _clientService.SoftDeleteAsync(id, cancellationToken);
        TempData["SuccessMessage"] = "Client soft-deleted.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q, bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var clients = await _clientService.ListAsync(q, cancellationToken: cancellationToken);
        return Json(clients
            .Where(c => !activeOnly || string.Equals(c.StatusCode, "A", StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(c => new
            {
                id = c.ClientID,
                text = string.IsNullOrWhiteSpace(c.ClientShortName) ? c.ClientName : $"{c.ClientName} ({c.ClientShortName})",
                statusCode = c.StatusCode,
                businessUnit = c.BusinessUnitName
            }));
    }
}
