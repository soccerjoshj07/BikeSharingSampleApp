﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// --------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using app.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace app.Controllers
{
    [Route("api/user")]
    public class UserController : Controller
    {
        private string _usersService { get; set; }

        private string _reservationsService { get; set; }

        private string _bikesService { get; set; }

        private string _billingService { get; set; }

        private CustomConfiguration _customConfiguration { get; set; }

        public UserController(IOptions<CustomConfiguration> customConfiguration)
        {
            _customConfiguration = customConfiguration.Value;
            _usersService = Environment.GetEnvironmentVariable(Constants.UsersMicroserviceEnv) ?? _customConfiguration.Services.Users;
            _reservationsService = Environment.GetEnvironmentVariable(Constants.ReservationsMicroserviceEnv) ?? _customConfiguration.Services.Reservations;
            _billingService = Environment.GetEnvironmentVariable(Constants.BillingMicroserviceEnv) ?? _customConfiguration.Services.Billing;
            _bikesService = Environment.GetEnvironmentVariable(Constants.BikesMicroserviceEnv) ?? _customConfiguration.Services.Bikes;
        }

        // GET: api/user/1
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUser(string userId)
        {
            if (!CheckValidUserId(userId))
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = "Invalid userId. The user Id is a valid Guid without the hyphens."
                };
            }

            string getUserUrl = $"http://{_usersService}/api/users/{userId}";
            var response = await HttpHelper.GetAsync(getUserUrl, this.Request);
            if (!response.IsSuccessStatusCode)
            {
                return await HttpHelper.ReturnResponseResult(response);
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var userDetails = JsonConvert.DeserializeObject<UserResponse>(responseBody);
            return new JsonResult(userDetails);
        }

        // GET: api/user/allUsers
        [HttpGet("allUsers")]
        public async Task<IActionResult> GetAllUsers()
        {
            string getAllUserUrl = $"http://{_usersService}/api/allUsers";
            var response = await HttpHelper.GetAsync(getAllUserUrl, this.Request);
            if (!response.IsSuccessStatusCode)
            {
                return await HttpHelper.ReturnResponseResult(response);
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var allUserDetails = JsonConvert.DeserializeObject<UserResponse[]>(responseBody);
            return new JsonResult(allUserDetails);
        }

        /// <summary>
        /// Returns null on success
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="updatedUser"></param>
        /// <returns></returns>
        private async Task<IActionResult> _UpdateUser(string userId, IUser updatedUser)
        {
            string getUpdateUserUrl = $"http://{_usersService}/api/users/{userId}";
            var getResponse = await HttpHelper.GetAsync(getUpdateUserUrl, this.Request);
            if (!getResponse.IsSuccessStatusCode)
            {
                return await HttpHelper.ReturnResponseResult(getResponse);
            }

            var existingUserDetails = JsonConvert.DeserializeObject<UserResponse>(await getResponse.Content.ReadAsStringAsync());
            if (existingUserDetails.Type != updatedUser.Type)
            {
                return BadRequest($"Tried to update a user who isn't a {updatedUser.Type.ToString()}");
            }

            var updatedUserDetails = new User
            {
                Name = string.IsNullOrEmpty(updatedUser.Name) ? existingUserDetails.Name : updatedUser.Name,
                Address = string.IsNullOrEmpty(updatedUser.Address) ? existingUserDetails.Address : updatedUser.Address,
                Phone = string.IsNullOrEmpty(updatedUser.Phone) ? existingUserDetails.Phone : updatedUser.Phone,
                Email = string.IsNullOrEmpty(updatedUser.Email) ? existingUserDetails.Email : updatedUser.Email
            };

            // Update user
            var updateResponse = await HttpHelper.PutAsync(getUpdateUserUrl, new StringContent(
                JsonConvert.SerializeObject(updatedUserDetails), Encoding.UTF8, "application/json"), this.Request);
            if (!updateResponse.IsSuccessStatusCode)
            {
                return await HttpHelper.ReturnResponseResult(updateResponse);
            }

            return null;
        }

        // PATCH: /api/user/vendor/{userId}
        [HttpPatch("vendor/{userId}")]
        public async Task<IActionResult> UpdateVendor(string userId, [FromBody] CreateVendorRequest vendorInput)
        {
            if (!CheckValidUserId(userId))
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = "Invalid userId. The user Id is a valid Guid without the hyphens."
                };
            }

            vendorInput.Type = UserType.Vendor;

            var updateUserResponse = await this._UpdateUser(userId, vendorInput);
            if (updateUserResponse != null)
            {
                return updateUserResponse;
            }

            // Now update Billing details
            var billingController = new BillingController(Options.Create(this._customConfiguration));
            var getVendorResponse = await billingController.GetVendor(userId);
            if (!(getVendorResponse is JsonResult))
            {
                var vendorContentResult = getVendorResponse as ContentResult;
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = $"Couldn't find vendor billing details: {vendorContentResult?.StatusCode} {vendorContentResult?.Content}"
                };
            }
            var currentVendor = (getVendorResponse as JsonResult)?.Value as Vendor;
            if (currentVendor == null)
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = "Unexpected object returned while getting vendor billing data"
                };
            }

            var updatedVendorDetails = new Vendor
            {
                UserID = userId,
                AccountNumber = string.IsNullOrEmpty(vendorInput.AccountNumber) ? currentVendor.AccountNumber : vendorInput.AccountNumber,
                RoutingNumber = string.IsNullOrEmpty(vendorInput.RoutingNumber) ? currentVendor.RoutingNumber : vendorInput.RoutingNumber
            };
            var updateVendorResponse = await billingController.UpdateVendor(updatedVendorDetails);
            if (!(updateVendorResponse is JsonResult))
            {
                var updateVendorContentResult = updateVendorResponse as ContentResult;
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = $"Couldn't update vendor billing details: {updateVendorContentResult?.StatusCode} {updateVendorContentResult?.Content}"
                };
            }
            var updatedVendor = (updateVendorResponse as JsonResult)?.Value as Vendor;
            if (updatedVendor == null)
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = $"Unexpected object returned while updating vendor billing data"
                };
            }

            return await GetUser(userId);
        }

        // PATCH: /api/user/1
        // Gets user details to fill in missing fields, updates and gets the updated user again.
        [HttpPatch("{userId}")]
        public async Task<IActionResult> UpdateCustomer(string userId, [FromBody] CreateCustomerRequest customerInput)
        {
            if (!CheckValidUserId(userId))
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = "Invalid userId. The user Id is a valid Guid without the hyphens."
                };
            }

            customerInput.Type = UserType.Customer;

            var updateUserResponse = await this._UpdateUser(userId, customerInput);
            if (updateUserResponse != null)
            {
                return updateUserResponse;
            }

            // Now update Billing details
            var billingController = new BillingController(Options.Create(this._customConfiguration));
            var getCustomerResponse = await billingController.GetCustomerBillingData(userId);
            if (!(getCustomerResponse is JsonResult))
            {
                var customerContentResult = getCustomerResponse as ContentResult;
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = $"Couldn't find customer billing details: {customerContentResult?.StatusCode} {customerContentResult?.Content}"
                };
            }
            var currentCustomer = (getCustomerResponse as JsonResult)?.Value as Customer;
            if (currentCustomer == null)
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = "Unexpected object returned while getting customer billing data"
                };
            }

            var updatedCustomerDetails = new Customer
            {
                UserID = userId,
                CCNumber = string.IsNullOrEmpty(customerInput.CCNumber) ? currentCustomer.CCNumber : customerInput.CCNumber,
                CCCCV = string.IsNullOrEmpty(customerInput.CCCCV) ? currentCustomer.CCCCV : customerInput.CCCCV,
                CCExpiry = string.IsNullOrEmpty(customerInput.CCExpiry) ? currentCustomer.CCExpiry : customerInput.CCExpiry
            };
            var updateCustomerResponse = await billingController.UpdateCustomerBillingData(updatedCustomerDetails);
            if (!(updateCustomerResponse is JsonResult))
            {
                var updateCustomerContentResult = updateCustomerResponse as ContentResult;
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = $"Couldn't update customer billing data: {updateCustomerContentResult?.StatusCode} {updateCustomerContentResult?.Content}"
                };
            }
            var updatedCustomer = (updateCustomerResponse as JsonResult)?.Value as Customer;
            if (updatedCustomer == null)
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = "Unexpected object returned while updating customer billing data"
                };
            }

            return await GetUser(userId);
        }

        public async Task<IActionResult> DeleteBike(string bikeId)
        {
            string getDeleteBikeUrl = $"http://{_bikesService}/api/bikes/{bikeId}";
            var getResponse = await HttpHelper.GetAsync(getDeleteBikeUrl, this.Request);
            if (getResponse.IsSuccessStatusCode)
            {
                // Bike exists, proceed with deletion.
                var deleteResponse = await HttpHelper.DeleteAsync(getDeleteBikeUrl, this.Request);
                return await HttpHelper.ReturnResponseResult(deleteResponse);
            }

            return await HttpHelper.ReturnResponseResult(getResponse);
        }

        // DELETE: /api/user/1
        [HttpDelete("{userId}")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            if (!CheckValidUserId(userId))
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = "Invalid userId. The user Id is a valid Guid without the hyphens."
                };
            }

            string getDeleteUserUrl = $"http://{_usersService}/api/users/{userId}";
            var getResponse = await HttpHelper.GetAsync(getDeleteUserUrl, this.Request);
            if (getResponse.IsSuccessStatusCode)
            {
                // User exists, proceed with deletion. Delete all bikes owned by user first.
                string getAllBikesUrl = $"http://{_bikesService}/api/allbikes";
                var listResponse = await HttpHelper.GetAsync(getAllBikesUrl, this.Request);
                if (listResponse.IsSuccessStatusCode)
                {
                    var foundBikes = JsonConvert.DeserializeObject<List<Bike>>(await listResponse.Content.ReadAsStringAsync());
                    var usersBikes = foundBikes.FindAll(bike => bike.OwnerUserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
                    var deleteBikesTasks = usersBikes.AsParallel().Select(bike => DeleteBike(bike.Id));
                    await Task.WhenAll(deleteBikesTasks);
                    var deleteResponse = await HttpHelper.DeleteAsync(getDeleteUserUrl, this.Request);
                    return await HttpHelper.ReturnResponseResult(deleteResponse);
                }

                return await HttpHelper.ReturnResponseResult(listResponse);
            }

            return await HttpHelper.ReturnResponseResult(getResponse);
        }

        private async Task<Tuple<HttpResponseMessage, string>> _CreateUser(User u)
        {
            string createUserUrl = $"http://{_usersService}/api/users";
            var userId = Guid.NewGuid().ToString("N");

            u.Id = string.IsNullOrEmpty(u.Id) ? userId : u.Id;
            Console.WriteLine("u.Id : " + u.Id);

            var response = await HttpHelper.PostAsync(createUserUrl, new StringContent(
                JsonConvert.SerializeObject(u), Encoding.UTF8, "application/json"), this.Request);
            return new Tuple<HttpResponseMessage, string>(response, u.Id);
        }

        // POST: /api/user
        [HttpPost]
        public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerRequest userInput)
        {
            if (!ModelState.IsValid)
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = JsonConvert.SerializeObject(ModelState.Values.SelectMany(v => v.Errors))
                };
            }

            userInput.Type = UserType.Customer;
            string userInputJson = JsonConvert.SerializeObject(userInput);
            User user = JsonConvert.DeserializeObject<User>(userInputJson);
            Customer cust = JsonConvert.DeserializeObject<Customer>(userInputJson);

            var createUserResponseObj = await this._CreateUser(user);
            var userResponse = createUserResponseObj.Item1;
            var userId = createUserResponseObj.Item2;
            if (!userResponse.IsSuccessStatusCode)
            {
                return await HttpHelper.ReturnResponseResult(userResponse);
            }

            cust.UserID = userId;

            var billingController = new BillingController(Options.Create(this._customConfiguration));
            var billingResponse = await billingController.AddCustomerBillingData(cust);
            if (!(billingResponse is JsonResult))
            {
                var contentResult = billingResponse as ContentResult;
                return new ContentResult()
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = $"Billing controller couldn't create customer billing data: {contentResult?.StatusCode} {contentResult?.Content}"
                };
            }

            return await GetUser(userId);
        }

        // POST: /api/user/vendor
        [HttpPost("vendor")]
        public async Task<IActionResult> CreateVendor([FromBody] CreateVendorRequest vendorInput)
        {
            if (!ModelState.IsValid)
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = JsonConvert.SerializeObject(ModelState.Values.SelectMany(v => v.Errors))
                };
            }

            vendorInput.Type = UserType.Vendor;
            string vendorInputJson = JsonConvert.SerializeObject(vendorInput);
            User user = JsonConvert.DeserializeObject<User>(vendorInputJson);
            Vendor vendor = JsonConvert.DeserializeObject<Vendor>(vendorInputJson);

            var createUserResponseObj = await this._CreateUser(user);
            var userResponse = createUserResponseObj.Item1;
            var userId = createUserResponseObj.Item2;
            if (!userResponse.IsSuccessStatusCode)
            {
                return await HttpHelper.ReturnResponseResult(userResponse);
            }

            vendor.UserID = userId;

            var billingController = new BillingController(Options.Create(this._customConfiguration));
            var billingResponse = await billingController.AddVendor(vendor);
            if (!(billingResponse is JsonResult))
            {
                var contentResult = billingResponse as ContentResult;
                return new ContentResult()
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Content = $"Billing controller couldn't create vendor billing data: {contentResult?.StatusCode} {contentResult?.Content}"
                };
            }

            return await GetUser(userId);
        }

        // POST: /api/user/auth
        [HttpPost("auth")]
        public async Task<IActionResult> Authenticate([FromBody] AuthenticateUserRequest credentials)
        {
            if (!ModelState.IsValid)
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = JsonConvert.SerializeObject(ModelState.Values.SelectMany(v => v.Errors))
                };
            }

            string authUserUrl = $"http://{_usersService}/api/users/auth";
            var response = await HttpHelper.PostAsync(authUserUrl, new StringContent(
                JsonConvert.SerializeObject(credentials), Encoding.UTF8, "application/json"), this.Request);
            if (response.IsSuccessStatusCode)
            {
                var user = JsonConvert.DeserializeObject<UserResponse>(await response.Content.ReadAsStringAsync());
                return await GetUser(user.Id);
            }

            return await HttpHelper.ReturnResponseResult(response);
        }

        // GET: /api/user/{userId}/bikes
        [HttpGet(@"{userId}/bikes")]
        public async Task<IActionResult> GetAllBikes(string userId)
        {
            if (!CheckValidUserId(userId))
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = "Invalid userId. The user Id is a valid Guid without the hyphens."
                };
            }

            string getAllBikesUrl = $"http://{_bikesService}/api/allbikes";
            var response = await HttpHelper.GetAsync(getAllBikesUrl, this.Request);
            if (response.IsSuccessStatusCode)
            {
                var foundBikes = JsonConvert.DeserializeObject<List<Bike>>(await response.Content.ReadAsStringAsync());
                var usersBikes = foundBikes.FindAll(bike => bike.OwnerUserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
                return new JsonResult(usersBikes);
            }

            return await HttpHelper.ReturnResponseResult(response);
        }

        [HttpGet(@"{userId}/reservations")]
        public async Task<IActionResult> ListReservations(string userId)
        {
            if (!CheckValidUserId(userId))
            {
                return new ContentResult
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Content = "Invalid userId. The user Id is a valid Guid without the hyphens."
                };
            }

            string listReservationsUrl = $"http://{_reservationsService}/api/user/{userId}/reservations";
            var response = await HttpHelper.GetAsync(listReservationsUrl, this.Request);
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var reservations = JsonConvert.DeserializeObject<List<Reservation>>(responseBody) ?? new List<Reservation>();
                foreach (var res in reservations)
                {
                    var addInvoiceDetailsResponse = await ReservationController._AddInvoiceDetailsToReservation(this._customConfiguration, res);
                    if (addInvoiceDetailsResponse != null)
                    {
                        return addInvoiceDetailsResponse;
                    }
                }
                return new JsonResult(reservations);
            }

            return await HttpHelper.ReturnResponseResult(response);
        }

        private static bool CheckValidUserId(string userId)
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParseExact(userId, "N", out Guid temp))
            {
                return false;
            }

            return true;
        }
    }
}