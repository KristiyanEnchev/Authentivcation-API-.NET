﻿namespace Infrastructure.Account.Services
{
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;

    using AutoMapper;
    using AutoMapper.QueryableExtensions;

    using Domain.Entities.Identity;

    using Shared;

    using Application.Handlers.Account.Common;
    using Application.Interfaces;
    using Application.Extensions;
    using Persistence.Constants;
    using Models.Enums;
    using MediatR;
    using Domain.Events;
    using Persistence.Contexts;

    public class UserService : IUserService
    {
        private readonly UserManager<User> userManager;
        private readonly IMapper _mapper;
        private readonly ITransactionHelper _transactionHelper;

        public UserService(UserManager<User> userManager, IMapper mapper, ITransactionHelper transactionHelper)
        {
            this.userManager = userManager;
            _mapper = mapper;
            _transactionHelper = transactionHelper;
        }

        public async Task<Result<List<UserResponseGetModel>>> GetListAsync(CancellationToken cancellationToken)
        {
            var users = await userManager.Users
                .AsNoTracking()
                .ProjectTo<UserResponseGetModel>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);

            foreach (var userResponse in users)
            {
                var user = await userManager.FindByIdAsync(userResponse.Id);
                var roles = await userManager.GetRolesAsync(user);
                var role = roles.FirstOrDefault();
                userResponse.Role = role;
            }

            return Result<List<UserResponseGetModel>>.SuccessResult(users);
        }

        public async Task<PaginatedResult<UserResponseGetModel>> GetPagedListAsync(
            int pageNumber,
            int pageSize,
            string sortBy,
            string order,
            CancellationToken cancellationToken)
        {
            var sortOrder = new UserSortOrder(sortBy, order);

            var paginatedAndSortedUsers = await userManager.Users
                .AsNoTracking()
                .Sort(sortOrder)
                .ProjectTo<UserResponseGetModel>(_mapper.ConfigurationProvider)
                .ToPaginatedListAsync(pageNumber, pageSize, cancellationToken);

            foreach (var userResponse in paginatedAndSortedUsers.Data)
            {
                var user = await userManager.FindByIdAsync(userResponse.Id);
                var roles = await userManager.GetRolesAsync(user);
                var role = roles.FirstOrDefault();
                userResponse.Role = role;
            }

            return paginatedAndSortedUsers;
        }

        public async Task<Result<UserResponseGetModel>> GetByIdAsync(string userId, CancellationToken cancellationToken)
        {
            var user = await userManager.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .ProjectTo<UserResponseGetModel>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                return Result<UserResponseGetModel>.Failure("User Not Found.");
            }

            return Result<UserResponseGetModel>.SuccessResult(user);
        }

        public async Task<Result<UserResponseGetModel>> GetByEmailAsync(string email, CancellationToken cancellationToken)
        {
            var user = await userManager.Users
                .AsNoTracking()
                .Where(u => u.Email == email)
                .ProjectTo<UserResponseGetModel>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                return Result<UserResponseGetModel>.Failure("User Not Found.");
            }

            return Result<UserResponseGetModel>.SuccessResult(user);
        }

        public async Task<Result<string>> ToggleStatusAsync(string value, ToggleUserValue toggleValue)
        {
            using (var transaction = await _transactionHelper.BeginTransactionAsync())
            {
                var user = await userManager.FindByEmailAsync(value) ?? await userManager.FindByIdAsync(value);
                if (user == null)
                {
                    return Result<string>.Failure("User not found.");
                }

                var changes = new List<string>();
                string propertyChanged = string.Empty;
                bool newValue = false;

                switch (toggleValue)
                {
                    case ToggleUserValue.IsActive:
                        user.IsActive = !user.IsActive;
                        propertyChanged = nameof(user.IsActive);
                        newValue = user.IsActive;
                        break;
                    case ToggleUserValue.IsEmailConfirmed:
                        user.EmailConfirmed = !user.EmailConfirmed;
                        propertyChanged = nameof(user.EmailConfirmed);
                        newValue = user.EmailConfirmed;
                        break;
                    case ToggleUserValue.IsLockedOut:
                        user.LockoutEnabled = !user.LockoutEnabled;
                        propertyChanged = nameof(user.LockoutEnabled);
                        newValue = user.LockoutEnabled;
                        break;
                }

                if (!string.IsNullOrEmpty(propertyChanged))
                {
                    changes.Add(propertyChanged);
                    var userToggleEvent = new UserToggleEvent(user.IsActive, user.EmailConfirmed, user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow, changes);
                    user.AddDomainEvent(userToggleEvent);
                }

                var result = await userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return Result<string>.Failure(result.Errors.Select(e => e.Description).ToList());
                }

                await transaction.CommitAsync();
                return Result<string>.SuccessResult($"{propertyChanged} toggled to {newValue}.");
            }
        }
    }
}
