using Billing.Interfaces;
using Billing.Models;

using Microsoft.AspNetCore.Mvc;

namespace Billing.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BillingController : ControllerBase
    {
        private readonly IBillingService _billingService;
        private readonly ILogger<BillingController> _logger;

        public BillingController(IBillingService billingService, ILogger<BillingController> logger)
        {
            _billingService = billingService;
            _logger = logger;
        }

        [HttpPost("deposit")]
        public async Task<IActionResult> Deposit([FromBody] DepositRequest request)
        {
            try
            {
                var result = await _billingService.DepositAsync(request);

                if (result)
                {
                    var balance = await _billingService.GetBalanceAsync(request.UserId);
                    return Ok(new { success = true, balance });
                }

                return BadRequest(new { error = "Deposit failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in deposit");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("withdraw")]
        public async Task<IActionResult> Withdraw([FromBody] WithdrawRequest request)
        {
            try
            {
                var result = await _billingService.WithdrawAsync(request);

                if (result)
                {
                    var balance = await _billingService.GetBalanceAsync(request.UserId);
                    return Ok(new { success = true, balance });
                }

                return BadRequest(new { error = "Withdrawal failed - insufficient funds" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in withdraw");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("balance/{userId}")]
        public async Task<IActionResult> GetBalance(long userId)
        {
            try
            {
                var balance = await _billingService.GetBalanceAsync(userId);
                return Ok(new { balance });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting balance");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }
}