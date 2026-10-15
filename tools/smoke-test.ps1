#requires -Version 7.0
[CmdletBinding()]
param(
    [uri]$BaseUrl = 'http://localhost:5245'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Checks = [System.Collections.Generic.List[object]]::new()
$script:RootUrl = $BaseUrl.AbsoluteUri.TrimEnd('/')

function Assert-Check {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $script:Checks.Add([pscustomobject]@{
        Result = $(if ($Passed) { 'PASS' } else { 'FAIL' })
        Check = $Name
        Detail = $Detail
    })
}

function Invoke-Api {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Path,
        [int[]]$Expected = @(200),
        [hashtable]$Account,
        [object]$Body,
        [switch]$Required
    )
    $parameters = @{
        Uri = "$script:RootUrl$Path"
        Method = $Method
        SkipHttpErrorCheck = $true
        TimeoutSec = 30
        Headers = @{ Accept = 'application/json' }
    }
    if ($Account -and $Account.accessToken) {
        $parameters.Headers.Authorization = "Bearer $($Account.accessToken)"
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ConvertTo-Json -InputObject $Body -Depth 10 -Compress
    }
    $response = Invoke-WebRequest @parameters
    $passed = [int]$response.StatusCode -in $Expected
    Assert-Check $Name $passed "HTTP $([int]$response.StatusCode); esperado $($Expected -join '/')"
    if (-not $passed -and $Required) {
        throw "Etapa obrigatória falhou: $Name (HTTP $([int]$response.StatusCode))."
    }
    if ($response.Content -and $passed) {
        $content = $response.Content
        if ($content -is [byte[]]) {
            $content = [Text.Encoding]::UTF8.GetString($content)
        }
        return ConvertFrom-Json -InputObject $content -AsHashtable -Depth 100
    }
    return $null
}

function Invoke-Login {
    param([string]$Name, [hashtable]$Account)
    $login = Invoke-Api -Name "Login $Name" -Method POST -Path '/login' -Body @{
        email = $Account.email
        password = $Account.password
    } -Required
    if (-not $login.accessToken) { throw "Login $Name não retornou accessToken." }
    $Account.accessToken = $login.accessToken
}

function New-Expense {
    param([string]$Name, [hashtable]$Account)
    $expense = Invoke-Api -Name "Criar $Name" -Method POST -Path '/api/expenses' -Expected 201 -Account $Account -Body $script:ExpenseBody -Required
    if (-not $expense.id) { throw "Criação $Name não retornou id." }
    Assert-State "Estado Draft: $Name" $expense.status 'Draft' 0
    return $expense.id
}

function Assert-State {
    param([string]$Name, [object]$Actual, [string]$State, [int]$Number)
    Assert-Check $Name ("$Actual" -eq $State -or "$Actual" -eq "$Number") "Esperado $State; recebido $Actual"
}

try {
    if ($BaseUrl.Scheme -notin @('http', 'https')) { throw 'BaseUrl deve usar HTTP ou HTTPS.' }
    $admin = @{
        email = $env:EXPENSEHUB_ADMIN_EMAIL
        password = $env:EXPENSEHUB_ADMIN_PASSWORD
        accessToken = $null
    }
    if ([string]::IsNullOrWhiteSpace($admin.email) -or [string]::IsNullOrWhiteSpace($admin.password)) {
        throw 'Defina EXPENSEHUB_ADMIN_EMAIL e EXPENSEHUB_ADMIN_PASSWORD antes de executar.'
    }
    Invoke-Login 'Admin' $admin
    $null = Invoke-Api -Name 'Anônimo em rota protegida' -Method GET -Path '/api/expenses' -Expected 401
    $null = Invoke-Api -Name 'Admin sem role funcional' -Method GET -Path '/api/expenses' -Expected 403 -Account $admin

    $roleMap = [ordered]@{
        employee = @('Employee')
        employee2 = @('Employee')
        approver = @('Approver')
        finance = @('Finance')
        auditor = @('Auditor')
        employeeApprover = @('Employee', 'Approver')
        employeeFinance = @('Employee', 'Finance')
        norole = @()
    }
    $accounts = @{}
    $suffix = [guid]::NewGuid().ToString('N')
    foreach ($name in $roleMap.Keys) {
        $account = @{
            email = "$name.$suffix@example.invalid"
            password = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + 'aA1!'
            accessToken = $null
        }
        $registration = @{ email = $account.email; password = $account.password }
        # Tentativa deliberada de overposting: o cadastro deve ignorar roles.
        if ($name -eq 'norole') { $registration.roles = @('Admin', 'Employee') }
        $null = Invoke-Api -Name "Registrar $name por HTTP" -Method POST -Path '/register' -Expected @(200, 201, 204) -Body $registration -Required
        Invoke-Login "$name antes das roles" $account
        $accounts[$name] = $account
    }

    $users = @(Invoke-Api -Name 'Admin lista usuários' -Method GET -Path '/api/admin/users' -Account $admin -Required)
    $adminUser = @($users | Where-Object { $_.email -eq $admin.email })
    if ($adminUser.Count -ne 1) { throw 'Admin autenticado não encontrado na listagem.' }
    foreach ($name in $roleMap.Keys) {
        $matches = @($users | Where-Object { $_.email -eq $accounts[$name].email })
        if ($matches.Count -ne 1) { throw "Usuário $name não encontrado na listagem Admin." }
        $accounts[$name].id = $matches[0].id
        if ($name -eq 'norole') {
            Assert-Check 'Register ignora roles enviadas' (@($matches[0].roles).Count -eq 0) 'Cadastro deve permanecer sem roles'
        }
        else {
            $null = Invoke-Api -Name "Admin atribui roles $name" -Method PUT -Path "/api/admin/users/$($matches[0].id)/roles" -Expected @(200, 204) -Account $admin -Body @{ roles = $roleMap[$name] } -Required
        }
        Invoke-Login "$name após atribuição" $accounts[$name]
    }
    $null = Invoke-Api -Name 'Sem role e register com roles ignoradas' -Method GET -Path '/api/expenses' -Expected 403 -Account $accounts.norole
    $null = Invoke-Api -Name 'Role inválida' -Method PUT -Path "/api/admin/users/$($accounts.employee.id)/roles" -Expected 400 -Account $admin -Body @{ roles = @('UnknownRole') }
    $null = Invoke-Api -Name 'Admin não remove própria Admin' -Method PUT -Path "/api/admin/users/$($adminUser[0].id)/roles" -Expected 400 -Account $admin -Body @{ roles = @() }

    $categories = @(Invoke-Api -Name 'Categorias autenticadas' -Method GET -Path '/api/categories' -Account $accounts.employee -Required)
    if ($categories.Count -eq 0) { throw 'Nenhuma categoria disponível.' }
    $script:ExpenseBody = @{
        description = 'Transporte para visita ao cliente'
        amount = 42.50
        expenseDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        categoryId = $categories[0].id
    }
    foreach ($invalid in @(
        @{ Name = 'Valor zero'; Field = 'amount'; Value = 0 },
        @{ Name = 'Data futura'; Field = 'expenseDate'; Value = [DateTime]::UtcNow.AddDays(2).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture) },
        @{ Name = 'Descrição curta'; Field = 'description'; Value = 'curta' }
    )) {
        $body = $script:ExpenseBody.Clone()
        $body[$invalid.Field] = $invalid.Value
        $null = Invoke-Api -Name $invalid.Name -Method POST -Path '/api/expenses' -Expected 400 -Account $accounts.employee -Body $body
    }
    $null = Invoke-Api -Name 'Auditor não cria despesa' -Method POST -Path '/api/expenses' -Expected 403 -Account $accounts.auditor -Body $script:ExpenseBody

    $paidId = New-Expense 'fluxo até Paid' $accounts.employee
    $draftId = New-Expense 'rascunho privado' $accounts.employee
    $null = Invoke-Api -Name 'Employee2 não lê despesa alheia' -Method GET -Path "/api/expenses/$paidId" -Expected 404 -Account $accounts.employee2
    $null = Invoke-Api -Name 'Approver não lê Draft alheio' -Method GET -Path "/api/expenses/$draftId" -Expected 404 -Account $accounts.approver
    $editedBody = $script:ExpenseBody.Clone()
    $editedBody.description = 'Transporte atualizado para visita ao cliente'
    $null = Invoke-Api -Name 'Editar Draft próprio' -Method PUT -Path "/api/expenses/$paidId" -Expected @(200, 204) -Account $accounts.employee -Body $editedBody -Required
    $null = Invoke-Api -Name 'Enviar Draft' -Method POST -Path "/api/expenses/$paidId/submit" -Expected @(200, 204) -Account $accounts.employee -Required
    $submitted = Invoke-Api -Name 'Approver lê Submitted' -Method GET -Path "/api/expenses/$paidId" -Account $accounts.approver -Required
    Assert-State 'Estado Submitted' $submitted.status 'Submitted' 1
    $approverList = @(Invoke-Api -Name 'Approver lista Submitted' -Method GET -Path '/api/expenses' -Account $accounts.approver -Required)
    Assert-Check 'Approver inclui Submitted e exclui Draft' (($paidId -in $approverList.id) -and ($draftId -notin $approverList.id)) 'Visibilidade aplicada na listagem'
    $null = Invoke-Api -Name 'Finance não paga Submitted' -Method POST -Path "/api/expenses/$paidId/pay" -Expected 409 -Account $accounts.finance
    $null = Invoke-Api -Name 'Reprovação com justificativa curta' -Method POST -Path "/api/expenses/$paidId/reject" -Expected 400 -Account $accounts.approver -Body @{ justification = 'curta' }
    $null = Invoke-Api -Name 'Aprovar Submitted' -Method POST -Path "/api/expenses/$paidId/approve" -Expected @(200, 204) -Account $accounts.approver -Required
    $approved = Invoke-Api -Name 'Finance lê Approved' -Method GET -Path "/api/expenses/$paidId" -Account $accounts.finance -Required
    Assert-State 'Estado Approved' $approved.status 'Approved' 2
    $null = Invoke-Api -Name 'Aprovação repetida' -Method POST -Path "/api/expenses/$paidId/approve" -Expected 409 -Account $accounts.approver
    $null = Invoke-Api -Name 'Pagar Approved' -Method POST -Path "/api/expenses/$paidId/pay" -Expected @(200, 204) -Account $accounts.finance -Required
    $null = Invoke-Api -Name 'Pagamento repetido' -Method POST -Path "/api/expenses/$paidId/pay" -Expected 409 -Account $accounts.finance
    $paid = Invoke-Api -Name 'Employee lê Paid próprio' -Method GET -Path "/api/expenses/$paidId" -Account $accounts.employee -Required
    Assert-State 'Estado Paid' $paid.status 'Paid' 4

    $rejectedId = New-Expense 'fluxo até Rejected' $accounts.employee
    $null = Invoke-Api -Name 'Enviar para reprovação' -Method POST -Path "/api/expenses/$rejectedId/submit" -Expected @(200, 204) -Account $accounts.employee -Required
    $reason = 'Comprovante apresentado não atende à política'
    $null = Invoke-Api -Name 'Reprovar Submitted' -Method POST -Path "/api/expenses/$rejectedId/reject" -Expected @(200, 204) -Account $accounts.approver -Body @{ justification = $reason } -Required
    $rejected = Invoke-Api -Name 'Auditor consulta Rejected' -Method GET -Path "/api/expenses/$rejectedId" -Account $accounts.auditor -Required
    Assert-State 'Estado Rejected' $rejected.status 'Rejected' 3

    $selfApproveId = New-Expense 'Employee+Approver' $accounts.employeeApprover
    $null = Invoke-Api -Name 'Employee+Approver envia própria' -Method POST -Path "/api/expenses/$selfApproveId/submit" -Expected @(200, 204) -Account $accounts.employeeApprover -Required
    $null = Invoke-Api -Name 'Autoaprovação proibida' -Method POST -Path "/api/expenses/$selfApproveId/approve" -Expected 403 -Account $accounts.employeeApprover
    $selfPayId = New-Expense 'Employee+Finance' $accounts.employeeFinance
    $null = Invoke-Api -Name 'Employee+Finance envia própria' -Method POST -Path "/api/expenses/$selfPayId/submit" -Expected @(200, 204) -Account $accounts.employeeFinance -Required
    $null = Invoke-Api -Name 'Approver aprova para Employee+Finance' -Method POST -Path "/api/expenses/$selfPayId/approve" -Expected @(200, 204) -Account $accounts.approver -Required
    $null = Invoke-Api -Name 'Autopagamento proibido' -Method POST -Path "/api/expenses/$selfPayId/pay" -Expected 403 -Account $accounts.employeeFinance
    $financeList = @(Invoke-Api -Name 'Finance lista despesas' -Method GET -Path '/api/expenses' -Account $accounts.finance -Required)
    $outsideScope = @($financeList | Where-Object { "$($_.status)" -notin @('Approved', 'Paid', '2', '4') })
    Assert-Check 'Finance lista somente Approved/Paid' ($outsideScope.Count -eq 0 -and $paidId -in $financeList.id -and $selfPayId -in $financeList.id) 'Inclui aprovado/pago; exclui outros estados'

    $history = @(Invoke-Api -Name 'Histórico do fluxo pago' -Method GET -Path "/api/expenses/$paidId/history" -Account $accounts.auditor -Required)
    $actions = @($history | ForEach-Object { "$($_.action)" })
    $expectedActions = @(@('Created', '0'), @('Updated', '1'), @('Submitted', '2'), @('Approved', '3'), @('Paid', '5'))
    $sequenceValid = $history.Count -eq $expectedActions.Count
    if ($sequenceValid) {
        for ($index = 0; $index -lt $expectedActions.Count; $index++) {
            $sequenceValid = $sequenceValid -and ($actions[$index] -in $expectedActions[$index])
        }
    }
    Assert-Check 'Histórico sem duplicação e na ordem' $sequenceValid 'Created, Updated, Submitted, Approved, Paid'
    $rejectedHistory = @(Invoke-Api -Name 'Histórico da reprovação' -Method GET -Path "/api/expenses/$rejectedId/history" -Account $accounts.auditor -Required)
    $decisions = @($rejectedHistory | Where-Object { "$($_.action)" -in @('Rejected', '4') })
    Assert-Check 'Histórico preserva justificativa' ($decisions.Count -eq 1 -and $decisions[0].justification -eq $reason) 'Uma reprovação com justificativa original'
}
catch {
    Assert-Check 'Execução interrompida' $false $_.Exception.Message
}

$script:Checks | Format-Table -AutoSize -Wrap | Out-Host
$failures = @($script:Checks | Where-Object Result -EQ 'FAIL').Count
Write-Host "$($script:Checks.Count - $failures) PASS; $failures FAIL"
if ($failures -gt 0) { exit 1 }
exit 0
