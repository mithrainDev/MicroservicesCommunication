using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PlatformService.AsyncDataServices;
using PlatformService.Data;
using PlatformService.Dtos;
using PlatformService.Models;
using PlatformService.SyncDataServices.Http;

namespace PlatformService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PlatformsController : ControllerBase
    {
        private readonly IPlatformRepo _repository;
        private readonly IMapper _mapper;
        private readonly ICommandDataClient _commandDataClient;
        private readonly IMessageBusClient _messageBusClient;
        private readonly ICustomerRepo _customerRepo;
        private readonly AppDbContext _context;

        public PlatformsController(
            IPlatformRepo repository,
            IMapper mapper,
            ICommandDataClient commandDataClient,
            IMessageBusClient messageBusClient,
            ICustomerRepo customerRepo, 
            AppDbContext context)
        {
            _repository = repository;
            _mapper = mapper;
            _commandDataClient = commandDataClient;
            _messageBusClient = messageBusClient;
            _customerRepo = customerRepo;
            _context = context;
        }

        [HttpGet]
        public ActionResult<IEnumerable<PlatformReadDto>> GetPlatforms()
        {
            Console.WriteLine("--> Getting Platforms....");

            var platformItem = _repository.GetAllPlatforms();

            return Ok(_mapper.Map<IEnumerable<PlatformReadDto>>(platformItem));
        }

        [HttpGet("{id}", Name = "GetPlatformById")]
        public ActionResult<PlatformReadDto> GetPlatformById(int id)
        {
            var platformItem = _repository.GetPlatformById(id);
            if (platformItem != null)
            {
                return Ok(_mapper.Map<PlatformReadDto>(platformItem));
            }

            return NotFound();
        }

        [HttpPost]
        public async Task<ActionResult<PlatformReadDto>> CreatePlatform(PlatformCreateDto platformCreateDto,
            string customerEmail)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Find or create the customer
                var customer = _customerRepo.FindByEmail(customerEmail);
                if (customer == null)
                {
                    customer = new Customer
                    {
                        Email = customerEmail,
                        FirstName = "Mock Name", // Mock data
                        LastName = "Mock Name", // Mock data
                    };
                    _context.Customers.Add(customer);
                }

                // Create the platform
                var platformModel = _mapper.Map<Platform>(platformCreateDto);
                platformModel.CustomerId = customer.Id;

                _context.Platforms.Add(platformModel);
                await _context.SaveChangesAsync();

                var platformReadDto = _mapper.Map<PlatformReadDto>(platformModel);

                // Send Sync Message
                try
                {
                    await _commandDataClient.SendPlatformToCommand(platformReadDto);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"--> Could not send synchronously: {ex.Message}");
                }

                // Send Async Message with retries
                var platformPublishedDto = _mapper.Map<PlatformPublishedDto>(platformReadDto);
                platformPublishedDto.Event = "Platform_Published";

                const int maxRetries = 5;
                var retryCount = 0;
                var delay = 1000;

                while (retryCount < maxRetries)
                {
                    try
                    {
                        _messageBusClient.PublishNewPlatform(platformPublishedDto);
                        Console.WriteLine("--> Async message sent successfully.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        Console.WriteLine($"--> Attempt {retryCount} failed to send async message: {ex.Message}");

                        if (retryCount == maxRetries)
                        {
                            throw new Exception($"--> Could not send async message after {maxRetries} attempts.", ex);
                        }

                        await Task.Delay(delay);
                        delay *= 2;
                    }
                }

                // Commit transaction
                await transaction.CommitAsync();

                return CreatedAtRoute(nameof(GetPlatformById), new { Id = platformReadDto.Id }, platformReadDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"--> Error occurred: {ex.Message}");
                await transaction.RollbackAsync();
                return StatusCode(500, "An error occurred while processing your request.");
            }

        }

    }
}