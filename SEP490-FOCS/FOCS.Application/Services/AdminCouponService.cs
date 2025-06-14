﻿using AutoMapper;
using FOCS.Application.DTOs.AdminServiceDTO;
using FOCS.Application.Services.Interface;
using FOCS.Common.Constants;
using FOCS.Common.Enums;
using FOCS.Common.Exceptions;
using FOCS.Common.Models;
using FOCS.Common.Utils;
using FOCS.Infrastructure.Identity.Common.Repositories;
using FOCS.Order.Infrastucture.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FOCS.Application.Services
{
    public class AdminCouponService : IAdminCouponService
    {
        private readonly IRepository<Coupon> _couponRepository;
        private readonly IRepository<Promotion> _promotionRepository;
        private readonly IMapper _mapper;

        private readonly ILogger<Coupon> _logger;

        public AdminCouponService(IRepository<Coupon> couponRepository, ILogger<Coupon> logger, IRepository<Promotion> promotionRepository, IMapper mapper)
        {
            _couponRepository = couponRepository;
            _logger = logger;
            _promotionRepository = promotionRepository;
            _mapper = mapper;
        }

        public async Task<CouponAdminDTO> CreateCouponAsync(CouponAdminDTO dto, string userId)
        {
            // Check userId empty
            ConditionCheck.CheckCondition(!string.IsNullOrEmpty(userId), AdminCoupon.UserIdEmpty);
            
            // Check coupon code type
            ConditionCheck.CheckCondition(dto.CouponType == CouponType.Manual 
                                                     || dto.CouponType == CouponType.AutoGenerate, 
                                                        AdminCoupon.CheckCouponCodeType);
            
            // Type 'auto' => Generate unique code
            string couponCode = dto.CouponType == CouponType.AutoGenerate ? await GenerateUniqueCouponCodeAsync()
                                                                         : dto.Code?.Trim() ?? "";
            
            // Check manual code empty
            ConditionCheck.CheckCondition(dto.CouponType != CouponType.AutoGenerate 
                                                     || !string.IsNullOrWhiteSpace(couponCode), 
                                                        AdminCoupon.CheckCouponCodeForManual);

            // Check unique code
            var existing = await _couponRepository.AsQueryable()
                                                  .AnyAsync(c => c.Code == couponCode && !c.IsDeleted);
            ConditionCheck.CheckCondition(!existing, AdminCoupon.CheckCreateUniqueCode);

            // Check dates
            ConditionCheck.CheckCondition(dto.StartDate < dto.EndDate, AdminCoupon.CheckCreateDate);

            // Map DTO to entity
            var newCoupon = _mapper.Map<Coupon>(dto);
            newCoupon.Id = Guid.NewGuid();
            
            newCoupon.MinimumItemQuantity = dto.SetCouponConditionRequest.ConditionType switch
            {
                CouponConditionType.MinItemsQuantity => dto.SetCouponConditionRequest.Value,
                _ => null
            };

            newCoupon.MinimumOrderAmount = dto.SetCouponConditionRequest.ConditionType switch
            {
                CouponConditionType.MinOrderAmount => dto.SetCouponConditionRequest.Value,
                _ => null
            };
            
            newCoupon.Code = couponCode;
            newCoupon.IsDeleted = false;
            newCoupon.CreatedAt = DateTime.UtcNow;
            newCoupon.CreatedBy = userId;

            await _couponRepository.AddAsync(newCoupon);
            await _couponRepository.SaveChangesAsync();

            return _mapper.Map<CouponAdminDTO>(newCoupon);
        }

        public async Task SetCouponConditionAsync(Guid couponId, SetCouponConditionRequest setCouponConditionRequest)
        {
            var coupon = await _couponRepository.GetByIdAsync(couponId);
            ConditionCheck.CheckCondition(coupon != null, FOCS.Common.Exceptions.Errors.Common.NotFound);

            try
            {
                coupon.MinimumItemQuantity = setCouponConditionRequest.ConditionType switch
                {
                    CouponConditionType.MinItemsQuantity => setCouponConditionRequest.Value,
                    _ => null
                };

                coupon.MinimumOrderAmount = setCouponConditionRequest.ConditionType switch
                {
                    CouponConditionType.MinOrderAmount => setCouponConditionRequest.Value,
                    _ => null
                };

                _couponRepository.Update(coupon);
                await _couponRepository.SaveChangesAsync();
            } catch(Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        private async Task<string> GenerateUniqueCouponCodeAsync()
        {
            const string chars = AdminCoupon.GenerateUniqueCouponCode;
            var random = new Random();

            string newCode;
            bool exists;

            do
            {
                newCode = new string(Enumerable.Repeat(chars, 8)
                    .Select(s => s[random.Next(s.Length)]).ToArray());

                exists = await _couponRepository.AsQueryable()
                    .AnyAsync(c => c.Code == newCode && !c.IsDeleted);

            } while (exists);

            return newCode;
        }

        public async Task<PagedResult<CouponAdminDTO>> GetAllCouponsAsync(UrlQueryParameters query, Guid storeId, string userId)
        {
            // Check userId is valid
            ConditionCheck.CheckCondition(!string.IsNullOrEmpty(userId), AdminCoupon.UserIdEmpty);
            ConditionCheck.CheckCondition(storeId != null, Errors.Common.StoreNotFound);

            var couponQuery = _couponRepository.AsQueryable().Where(c => !c.IsDeleted && c.StoreId == storeId);

            // Search
            if (!string.IsNullOrEmpty(query.SearchBy) && !string.IsNullOrEmpty(query.SearchValue))
            {
                var value = query.SearchValue.ToLower();
                switch (query.SearchBy.ToLower())
                {
                    case AdminCoupon.SearchByCode:
                        couponQuery = couponQuery.Where(c => c.Code.ToLower().Contains(value));
                        break;
                    case AdminCoupon.SearchByDescription:
                        couponQuery = couponQuery.Where(c => c.Description.ToLower().Contains(value));
                        break;
                    case AdminCoupon.SearchByDiscountType:
                        if (Enum.TryParse<DiscountType>(query.SearchValue, true, out var type))
                            couponQuery = couponQuery.Where(c => c.DiscountType == type);
                        break;
                }
            }

            // Sort
            if (!string.IsNullOrEmpty(query.SortBy))
            {
                bool desc = query.SortOrder?.ToLower() == AdminCoupon.SortOrder;
                couponQuery = query.SortBy.ToLower() switch
                {
                    AdminCoupon.SortByCode => desc ? couponQuery.OrderByDescending(c => c.Code) : couponQuery.OrderBy(c => c.Code),
                    AdminCoupon.SortByValue => desc ? couponQuery.OrderByDescending(c => c.Value) : couponQuery.OrderBy(c => c.Value),
                    AdminCoupon.SortByStartDate => desc ? couponQuery.OrderByDescending(c => c.StartDate) : couponQuery.OrderBy(c => c.StartDate),
                    AdminCoupon.SortByEndDate => desc ? couponQuery.OrderByDescending(c => c.EndDate) : couponQuery.OrderBy(c => c.EndDate),
                    AdminCoupon.SortByIsActive => desc ? couponQuery.OrderByDescending(c => c.IsActive) : couponQuery.OrderBy(c => c.IsActive),
                    _ => couponQuery
                };
            }

            // Pagination
            var total = await couponQuery.CountAsync();
            var items = await couponQuery
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            var mapped = _mapper.Map<List<CouponAdminDTO>>(items);
            return new PagedResult<CouponAdminDTO>(mapped, total, query.Page, query.PageSize);
        }

        public async Task<bool> UpdateCouponAsync(Guid id, CouponAdminDTO dto, string userId)
        {
            var coupon = await _couponRepository.GetByIdAsync(id);
            if (coupon == null || coupon.IsDeleted)
                return false;

            // Check unique code (exclude current coupon)
            var existing = await _couponRepository.AsQueryable()
                                                  .AnyAsync(c => c.Id != id && c.Code == dto.Code && !c.IsDeleted);
            ConditionCheck.CheckCondition(!existing, AdminCoupon.CheckUpdateUniqueCode);

            // Check dates
            ConditionCheck.CheckCondition(dto.StartDate <= dto.EndDate, AdminCoupon.CheckUpdateDate);

            _mapper.Map(dto, coupon);
            coupon.UpdatedAt = DateTime.UtcNow;
            coupon.UpdatedBy = userId;

            await _couponRepository.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteCouponAsync(Guid id, string userId)
        {
            var coupon = await _couponRepository.GetByIdAsync(id);
            if (coupon == null || coupon.IsDeleted)
                return false;

            coupon.IsDeleted = true;
            coupon.UpdatedAt = DateTime.UtcNow;
            coupon.UpdatedBy = userId;

            await _couponRepository.SaveChangesAsync();
            return true;
        }

        public async Task<int> TrackCouponUsageAsync(Guid couponId)
        {
            var coupon = await _couponRepository.GetByIdAsync(couponId);
            if (coupon == null || coupon.IsDeleted || !coupon.IsActive)
                return -1;

            var now = DateTime.UtcNow;
            if (now <= coupon.StartDate || now >= coupon.EndDate)
                return -1;

            if (coupon.CountUsed >= coupon.MaxUsage)
                return -1;

            return coupon.MaxUsage - coupon.CountUsed;
        }


        public async Task<bool> SetCouponStatusAsync(Guid couponId, bool isActive, string userId)
        {
            var coupon = await _couponRepository.GetByIdAsync(couponId);
            if (coupon == null || coupon.IsDeleted)
                return false;

            coupon.IsActive = isActive;
            coupon.UpdatedAt = DateTime.UtcNow;
            coupon.UpdatedBy = userId;

            await _couponRepository.SaveChangesAsync();
            return true;
        }

        public async Task<bool> AssignCouponsToPromotionAsync(List<Guid> couponIds, Guid promotionId, string userId, Guid storeId)
        {
            // Get promotion
            var promotion = await _promotionRepository.GetByIdAsync(promotionId);
            ConditionCheck.CheckCondition(promotion != null && !promotion.IsDeleted, AdminCoupon.CheckPromotion);

            // Get coupons
            var coupons = await _couponRepository.FindAsync(c => couponIds.Contains(c.Id) && !c.IsDeleted);

            foreach (var coupon in coupons)
            {
                // Check exists store
                ConditionCheck.CheckCondition(coupon.StoreId == storeId, $"Coupon {coupon.Code} does not belong to this store.");

                // Check coupon dates within promotion dates
                ConditionCheck.CheckCondition(coupon.StartDate >= promotion.StartDate 
                                           && coupon.EndDate <= promotion.EndDate,
                                              $"Coupon {coupon.Code} must be within the promotion period.");

                // Gán PromotionId
                coupon.PromotionId = promotionId;
                coupon.UpdatedAt = DateTime.UtcNow;
                coupon.UpdatedBy = userId.ToString();
            }

            await _couponRepository.SaveChangesAsync();
            return true;
        }


    }
}
