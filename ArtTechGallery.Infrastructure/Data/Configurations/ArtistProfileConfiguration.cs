using ArtTechGallery.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtTechGallery.Infrastructure.Data.Configurations;

public class ArtistProfileConfiguration : IEntityTypeConfiguration<ArtistProfile>
{
    public void Configure(EntityTypeBuilder<ArtistProfile> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Bio)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.ProfileImageUrl)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.ShareCode)
            .IsRequired()
            .HasMaxLength(16);

        builder.HasIndex(x => x.ShareCode)
            .IsUnique();

        builder.HasIndex(x => x.UserId)
            .IsUnique();

        builder.HasOne(x => x.User)
            .WithOne(x => x.ArtistProfile)
            .HasForeignKey<ArtistProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Exhibitions)
            .WithOne(x => x.ArtistProfile)
            .HasForeignKey(x => x.ArtistProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}