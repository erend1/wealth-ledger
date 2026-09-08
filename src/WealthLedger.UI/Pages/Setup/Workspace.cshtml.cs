using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Setup;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Portfolios;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Setup;

[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.WorkspaceUninitialized)]
public sealed class WorkspaceModel : PageModel
{
    private readonly GetCoreLedgerSetupStateUseCase _getSetupState;
    private readonly InitializeCoreLedgerUseCase _initializeCoreLedger;
    private readonly UiText _text;

    public WorkspaceModel(
        GetCoreLedgerSetupStateUseCase getSetupState,
        InitializeCoreLedgerUseCase initializeCoreLedger,
        ValuePresenter valuePresenter,
        UiText text)
    {
        _getSetupState = getSetupState
            ?? throw new ArgumentNullException(nameof(getSetupState));
        _initializeCoreLedger = initializeCoreLedger
            ?? throw new ArgumentNullException(nameof(initializeCoreLedger));
        ArgumentNullException.ThrowIfNull(valuePresenter);
        _text = text ?? throw new ArgumentNullException(nameof(text));

        InstitutionTypes = Enum
            .GetValues<InstitutionType>()
            .Select(value => StableCodeOption.Create(value, valuePresenter))
            .ToArray();
        AccountTypes = Enum
            .GetValues<AccountType>()
            .Select(value => StableCodeOption.Create(value, valuePresenter))
            .ToArray();
    }

    [BindProperty]
    public WorkspaceSetupForm Input { get; set; } = new();

    public IReadOnlyList<StableCodeOption> InstitutionTypes { get; }

    public IReadOnlyList<StableCodeOption> AccountTypes { get; }

    public bool WorkspaceComplete { get; private set; }

    public bool WorkspaceUnavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        await LoadSetupStateAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        var state = await LoadSetupStateAsync(cancellationToken);

        if (WorkspaceComplete)
        {
            return RedirectToPage();
        }

        if (WorkspaceUnavailable
            || state != CoreLedgerSetupState.Empty)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            return Page();
        }

        var mapping = WorkspaceSetupFormMapper.Map(Input);

        if (!mapping.Succeeded)
        {
            foreach (var error in mapping.Errors)
            {
                ModelState.AddModelError(
                    $"{nameof(Input)}.{error.FieldName}",
                    _text[error.ResourceKey]);
            }

            return Page();
        }

        try
        {
            await _initializeCoreLedger.ExecuteAsync(
                mapping.Command!,
                cancellationToken);

            return RedirectToPage();
        }
        catch (CoreLedgerAlreadyInitializedException)
        {
            await LoadSetupStateAsync(cancellationToken);

            if (WorkspaceComplete)
            {
                return RedirectToPage();
            }

            AddOperationError("Workspace_Error_AlreadyInitializedConflict");
        }
        catch (CoreLedgerSetupUnavailableException exception)
        {
            AddOperationError(
                exception.Category
                    == LocalDataFailureCategory.OwnershipBusy
                        ? "Workspace_Error_OwnershipBusy"
                        : "Workspace_Error_SetupUnavailable");
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or ApplicationRuleViolationException
                  or DomainRuleViolationException)
        {
            ModelState.AddModelError(
                string.Empty,
                _text["Workspace_Error_InvalidInput"]);
        }
        catch (CoreLedgerPersistenceException)
        {
            AddOperationError("Workspace_Error_PersistenceConflict");
        }

        return Page();
    }

    private async Task<CoreLedgerSetupState?> LoadSetupStateAsync(
        CancellationToken cancellationToken)
    {
        var result = await _getSetupState.ExecuteAsync(
            cancellationToken);

        if (!result.Succeeded || result.Value is null)
        {
            WorkspaceComplete = false;
            WorkspaceUnavailable = true;
            ModelState.AddModelError(
                string.Empty,
                _text["Workspace_Error_StateUnavailable"]);
            return null;
        }

        WorkspaceComplete = result.Value.State
                            == CoreLedgerSetupState.Complete;
        WorkspaceUnavailable = result.Value.State
                               == CoreLedgerSetupState.PartialOrConflicting;

        if (WorkspaceUnavailable)
        {
            ModelState.AddModelError(
                string.Empty,
                _text["Workspace_Error_PartialState"]);
        }

        return result.Value.State;
    }

    private void AddOperationError(string resourceKey)
    {
        ModelState.AddModelError(
            string.Empty,
            _text[resourceKey]);
        Response.StatusCode = StatusCodes.Status409Conflict;
    }
}

public sealed record StableCodeOption(
    string Code,
    string Text)
{
    internal static StableCodeOption Create(
        InstitutionType value,
        ValuePresenter presenter)
    {
        var display = presenter.StableCode(value);
        return new StableCodeOption(
            StableCodes.ToCode(value),
            display.Text);
    }

    internal static StableCodeOption Create(
        AccountType value,
        ValuePresenter presenter)
    {
        var display = presenter.StableCode(value);
        return new StableCodeOption(
            StableCodes.ToCode(value),
            display.Text);
    }
}
