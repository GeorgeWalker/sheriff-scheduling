﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SS.Api.controllers.scheduling;
using SS.Api.helpers.extensions;
using SS.Api.infrastructure.exceptions;
using SS.Api.Models.DB;
using SS.Api.models.dto.generated;
using SS.Api.services.scheduling;
using SS.Api.services.usermanagement;
using SS.Db.models.scheduling;
using SS.Db.models.sheriff;
using tests.api.helpers;
using tests.api.Helpers;
using Xunit;

namespace tests.controllers
{
    public class ShiftControllerTests : WrapInTransactionScope
    {
        private ShiftController ShiftController { get; }
        public ShiftControllerTests() : base(false)
        {
            ShiftController = new ShiftController(new ShiftService(Db, new SheriffService(Db)))
            {
                ControllerContext = HttpResponseTest.SetupMockControllerContext()
            };
        }

        [Fact]
        public async Task AssignToShifts()
        {
            var sheriffId = Guid.NewGuid();
            await Db.Location.AddAsync(new Location { Id = 1, AgencyId = "zz" });
            await Db.Sheriff.AddAsync(new Sheriff { Id = sheriffId });

            await Db.Shift.AddAsync(new Shift
            {
                Id = 1, StartDate = DateTimeOffset.UtcNow.Date, 
                EndDate = DateTimeOffset.UtcNow.Date.AddHours(1),
                LocationId = 1,
                Timezone = "America/Vancouver"
            });
            await Db.Shift.AddAsync(new Shift
            {
                Id = 2, StartDate = DateTimeOffset.UtcNow.Date.AddHours(1),
                EndDate = DateTimeOffset.UtcNow.Date.AddHours(2), 
                LocationId = 1,
                Timezone = "America/Vancouver"
            });
            await Db.Shift.AddAsync(new Shift
            {
                Id = 3, StartDate = DateTimeOffset.UtcNow.Date, EndDate = DateTimeOffset.UtcNow.Date.AddHours(1),
                LocationId = 1,
                Timezone = "America/Vancouver"
            });
            await Db.Shift.AddAsync(new Shift
            {
                Id = 4, StartDate = DateTimeOffset.UtcNow.Date.AddHours(1),
                EndDate = DateTimeOffset.UtcNow.Date.AddHours(2), LocationId = 1,
                Timezone = "America/Vancouver"
            });
            await Db.Shift.AddAsync(new Shift
            {
                Id = 5, StartDate = DateTimeOffset.UtcNow.Date, EndDate = DateTimeOffset.UtcNow.Date.AddHours(2),
                LocationId = 1,
                Timezone = "America/Vancouver"
            });
            await Db.Shift.AddAsync(new Shift
            {
                Id = 6, StartDate = DateTimeOffset.UtcNow.Date.AddHours(2),
                EndDate = DateTimeOffset.UtcNow.Date.AddHours(1), LocationId = 1,
                Timezone = "America/Vancouver"
            });
            await Db.Shift.AddAsync(new Shift
            {
                Id = 7, StartDate = DateTimeOffset.UtcNow.Date.AddHours(-1), EndDate = DateTimeOffset.UtcNow.Date,
                LocationId = 1,
                Timezone = "America/Vancouver"
            });

            await Db.SaveChangesAsync();

            Detach();

            //Case where shifts conflict with themselves.
            var shiftIds = new List<int> { 1, 5 };
            await Assert.ThrowsAsync<BusinessLayerException>(async () => await ShiftController.AssignToShifts(shiftIds, sheriffId, false));
            var sheriffShifts = Db.Shift.AsNoTracking().Where(s => s.SheriffId == sheriffId);
            Assert.Empty(sheriffShifts);

            //Two shifts no conflicts.
            shiftIds = new List<int> { 1, 2 };
            var shifts = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AssignToShifts(shiftIds, sheriffId, false));
            sheriffShifts = Db.Shift.AsNoTracking().Where(s => s.SheriffId == sheriffId);
            Assert.All(sheriffShifts, s => new List<int> { 1, 2 }.Contains(s.Id));

            //Already assigned to two shifts: Two new shifts, should conflict now. 
            shiftIds = new List<int> { 3, 4 };
            await Assert.ThrowsAsync<BusinessLayerException>(async () => await ShiftController.AssignToShifts(shiftIds, sheriffId, false));
            sheriffShifts = Db.Shift.AsNoTracking().Where(s => s.SheriffId == sheriffId);
            Assert.All(sheriffShifts, s => new List<int> { 1, 2 }.Contains(s.Id));

            //Override, should remove from existing shifts and place into new shifts - instead of erroring and saying there is a conflict. 
            HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AssignToShifts(shiftIds, sheriffId, true));
            sheriffShifts = Db.Shift.AsNoTracking().Where(s => s.SheriffId == sheriffId);
            Assert.All(sheriffShifts, s => new List<int> { 3, 4 }.Contains(s.Id));

            //Schedule two more shifts, on the outside of 3 and 4. 
            shiftIds = new List<int> { 6, 7 };
            HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AssignToShifts(shiftIds, sheriffId, true));
            sheriffShifts = Db.Shift.AsNoTracking().Where(s => s.SheriffId == sheriffId);
            Assert.All(sheriffShifts, s => new List<int> { 3, 4, 6, 7 }.Contains(s.Id));
        }


        [Fact]
        public async Task GetAvailability()
        {
            await Db.Location.AddAsync(new Location { Id = 1, AgencyId = "zz", Name = "Location 1" });
            await Db.Location.AddAsync(new Location { Id = 2, AgencyId = "5555", Name = "Location 2" });
            await Db.SaveChangesAsync();

            var startDate = DateTimeOffset.UtcNow.ConvertToTimezone("America/Edmonton");
            var endDate = DateTimeOffset.UtcNow.TranslateDateIfDaylightSavings("America/Edmonton", 7);

            //On awayLocation.
            await Db.Sheriff.AddAsync(new Sheriff
            {
                Id = Guid.NewGuid(),
                IsEnabled = true,
                AwayLocation = new List<SheriffAwayLocation>
                {
                    new SheriffAwayLocation
                    {
                        StartDate = startDate,
                        EndDate = startDate.AddDays(1),
                        LocationId = 2
                    }
                }
            });

            //On training.
            await Db.Sheriff.AddAsync(new Sheriff
            {
                Id = Guid.NewGuid(),
                IsEnabled = true,
                Training = new List<SheriffTraining>
                {
                    new SheriffTraining
                    {
                        StartDate = startDate.AddDays(1),
                        EndDate = startDate.AddDays(2)
                    }
                }
            });

            //On leave.
            await Db.Sheriff.AddAsync(new Sheriff
            {
                Id = Guid.NewGuid(),
                IsEnabled = true,
                Leave = new List<SheriffLeave>
                {
                    new SheriffLeave
                    {
                        StartDate = startDate.AddDays(1),
                        EndDate = startDate.AddDays(2)
                    }
                }
            });

            //Already scheduled.
            var scheduledSheriff = Guid.NewGuid();
            await Db.Sheriff.AddAsync(new Sheriff
            {
                Id = scheduledSheriff,
                IsEnabled = true
            });

            await Db.Shift.AddAsync(new Shift
            {
                Id = 1,
                StartDate = startDate.AddDays(2),
                EndDate = startDate.AddDays(3),
                LocationId = 1,
                Timezone = "America/Vancouver"
            });


            //Already scheduled different location.
            var scheduledSheriff2 = Guid.NewGuid();
            await Db.Sheriff.AddAsync(new Sheriff
            {
                Id = scheduledSheriff2,
                IsEnabled = true
            });

            await Db.Shift.AddAsync(new Shift
            {
                Id = 2,
                StartDate = startDate.AddDays(2),
                EndDate = startDate.AddDays(3),
                LocationId = 2,
                Timezone = "America/Vancouver"
            });

            var expiredShiftSheriffId = Guid.NewGuid();
            //Expired Leave, Expired Training, Expired Away Location, Expired Shift
            await Db.Sheriff.AddAsync(new Sheriff
            {
                Id = expiredShiftSheriffId,
                IsEnabled = true,
                Leave = new List<SheriffLeave>
                {
                    new SheriffLeave
                    {
                        StartDate = startDate.AddDays(1),
                        EndDate = startDate.AddDays(2),
                        ExpiryDate = DateTimeOffset.UtcNow
                    }
                },
                Training = new List<SheriffTraining>
                {
                    new SheriffTraining
                    {
                        StartDate = startDate.AddDays(1),
                        EndDate = startDate.AddDays(2),
                        ExpiryDate = DateTimeOffset.UtcNow
                    }
                },
                AwayLocation = new List<SheriffAwayLocation>
                {
                    new SheriffAwayLocation
                    {
                        StartDate = startDate,
                        EndDate = startDate.AddDays(1),
                        LocationId = 2,
                        ExpiryDate = DateTimeOffset.UtcNow
                    }
                }
            });

            await Db.Shift.AddAsync(new Shift
            {
                Id = 3,
                StartDate = startDate.AddDays(2),
                EndDate = startDate.AddDays(3),
                LocationId = 2,
                SheriffId = expiredShiftSheriffId,
                Timezone = "America/Vancouver",
                ExpiryDate = DateTimeOffset.UtcNow
            });

            //Expired Sheriff
            await Db.Sheriff.AddAsync(new Sheriff
            {
                Id = Guid.NewGuid(),
                FirstName = "Expired",
                LastName = "Expired Sheriff",
                IsEnabled = false
            });

            await Db.SaveChangesAsync();
            await ShiftController.GetAvailability(1, startDate, endDate);
        }

        [Fact]
        public async Task GetShifts()
        {
            var edmontonTz = DateTimeZoneProviders.Tzdb["America/Edmonton"];
            var startDateNowEdmonton = SystemClock.Instance.GetCurrentInstant().InZone(edmontonTz);
            var endDateNowEdmonton = SystemClock.Instance.GetCurrentInstant().InZone(edmontonTz).PlusHours(24*5);

            var startTimeOffset = startDateNowEdmonton.ToDateTimeOffset();
            var endTimeOffset = endDateNowEdmonton.ToDateTimeOffset();

            await Db.Shift.AddAsync(new Shift
            {
                Id = 1, 
                Type = ShiftType.Courts,
                StartDate = startTimeOffset,
                EndDate = endTimeOffset,
                Sheriff = new Sheriff { Id = Guid.NewGuid(), LastName = "hello" },
                AnticipatedAssignment = new Assignment {Id = 1, Name = "Super assignment", Location = new Location { Id = 1, AgencyId = "zz"}},
                LocationId = 1
            });

            await Db.SaveChangesAsync();

            var response = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.GetShifts(1, startTimeOffset, endTimeOffset));
            Assert.NotEmpty(response);
            Assert.NotNull(response[0].Sheriff);
            Assert.NotNull(response[0].AnticipatedAssignment);
            Assert.NotNull(response[0].Location);
        }

        [Fact]
        public async Task AddShift()
        {
            var shiftDto = await CreateShift();
            var shift = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AddShift(shiftDto));
        }

        [Fact]
        public async Task UpdateShift()
        {
            var shiftDto = await CreateShift();
            var sheriffId = Guid.NewGuid();
            await Db.Sheriff.AddAsync(new Sheriff { Id = sheriffId, FirstName = "Hello", LastName = "There"});
            await Db.Assignment.AddAsync(new Assignment { Id = 5, LocationId = 1 });
            await Db.SaveChangesAsync();

            var shift = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AddShift(shiftDto));
            shift.Type = ShiftType.Escorts;
            shift.StartDate = DateTimeOffset.UtcNow.AddDays(5).Date;
            shift.EndDate = DateTimeOffset.UtcNow.AddDays(6).Date;
            shift.LocationId = 5; // This shouldn't change 
            shift.ExpiryDate = DateTimeOffset.UtcNow; // this shouldn't change
            shift.SheriffId = sheriffId;
            shift.Sheriff = new SheriffDto(); // shouldn't change
            shift.Duties = new List<DutyDto>(); // shouldn't change
            shift.AnticipatedAssignment = new AssignmentDto(); //this shouldn't create new. 
            shift.AnticipatedAssignmentId = 5;
            shift.Location = new LocationDto(); // shouldn't change
            shift.LocationId = 5555; // shouldn't change

            var updatedShift = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.UpdateShift(shift));

            Assert.Equal(shiftDto.LocationId,updatedShift.LocationId);
            Assert.Null(updatedShift.ExpiryDate);
            Assert.Equal(5, updatedShift.AnticipatedAssignmentId);
            Assert.Equal(sheriffId, updatedShift.SheriffId);

            //Create the same shift, without sheriff, should conflict.
            shiftDto.SheriffId = null;
            shiftDto.StartDate = DateTimeOffset.UtcNow.AddDays(5).Date;
            shiftDto.EndDate = DateTimeOffset.UtcNow.AddDays(6).Date;
            shift = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AddShift(shiftDto));

            shift.SheriffId = sheriffId;
            await Assert.ThrowsAsync<BusinessLayerException>(() => ShiftController.UpdateShift(shift));

            //Create a shift that sits side by side, without sheriff, shouldn't conflict.
            shiftDto.SheriffId = null;
            shiftDto.StartDate = DateTimeOffset.UtcNow.AddDays(4).Date;
            shiftDto.EndDate = DateTimeOffset.UtcNow.AddDays(5).Date;
            shift = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AddShift(shiftDto));

            shift.SheriffId = sheriffId;
            updatedShift = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.UpdateShift(shift));

            Assert.Equal(shiftDto.StartDate, updatedShift.StartDate);
            Assert.Equal(shiftDto.EndDate, updatedShift.EndDate);
        }



        [Fact]
        public async Task RemoveShift()
        {
            var shiftDto = await CreateShift();
            var shift = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AddShift(shiftDto));

            HttpResponseTest.CheckForNoContentResponse(await ShiftController.ExpireShift(shift.Id));

            var response = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.GetShifts(1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(5)));
            Assert.Empty(response);
        }

        [Fact]
        public async Task ImportWeeklyShifts()
        {
            var sheriffId = Guid.NewGuid();
            var shiftDto = await CreateShift();
            await Db.Sheriff.AddAsync(new Sheriff {Id = sheriffId});
            shiftDto.StartDate = DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek - 6); //Last week monday
            shiftDto.EndDate = DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek - 5); //Last week tuesday
            shiftDto.SheriffId = sheriffId;

            var shift = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(await ShiftController.AddShift(shiftDto));

            var importedShifts = HttpResponseTest.CheckForValid200HttpResponseAndReturnValue(
                await ShiftController.ImportWeeklyShifts(1, true));

            Assert.NotNull(importedShifts);
            var importedShift = importedShifts.First();
            Assert.Equal(shiftDto.StartDate.AddDays(7).DateTime,importedShift.StartDate.DateTime, TimeSpan.FromSeconds(10)); //This week monday
            Assert.Equal(shiftDto.EndDate.AddDays(7).DateTime, importedShift.EndDate.DateTime, TimeSpan.FromSeconds(10)); //This week monday
            Assert.Equal(shiftDto.SheriffId, importedShift.SheriffId);
        }


        private async Task<ShiftDto> CreateShift()
        {
            var sheriffId = Guid.NewGuid();
            await Db.Location.AddAsync(new Location { Id = 1, AgencyId = "zz", Timezone = "America/Vancouver"});
            await Db.Sheriff.AddAsync(new Sheriff { Id = sheriffId, FirstName = "First", LastName = "Sheriff"});
            await Db.SaveChangesAsync();

            var shiftDto = new ShiftDto
            {
                ExpiryDate = DateTimeOffset.UtcNow, // should be null.
                SheriffId = sheriffId, // should be null.
                Type = ShiftType.Jail,
                StartDate = DateTimeOffset.UtcNow,
                EndDate = DateTimeOffset.UtcNow.AddHours(5),
                Sheriff = new SheriffDto(),
                AnticipatedAssignment = null,
                Location = new LocationDto { Id = 55, AgencyId = "55" },
                LocationId = 1,
                Duties = new List<DutyDto>(),
                Timezone = "America/Edmonton"
            };
            return shiftDto;
        }

    }
}