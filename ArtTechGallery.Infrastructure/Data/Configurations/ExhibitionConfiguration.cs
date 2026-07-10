using ArtTechGallery.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtTechGallery.Infrastructure.Data.Configurations;

public class ExhibitionConfiguration : IEntityTypeConfiguration<Exhibition>
{
    public void Configure(EntityTypeBuilder<Exhibition> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.ShareCode)
            .IsRequired()
            .HasMaxLength(16);

        builder.HasIndex(x => x.ShareCode)
            .IsUnique();

        builder.HasOne(x => x.ArtistProfile)
            .WithMany(x => x.Exhibitions)
            .HasForeignKey(x => x.ArtistProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Artworks)
            .WithOne(x => x.Exhibition)
            .HasForeignKey(x => x.ExhibitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}