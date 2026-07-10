using ArtTechGallery.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtTechGallery.Infrastructure.Data.Configurations;

public class ArtworkConfiguration : IEntityTypeConfiguration<Artwork>
{
    public void Configure(EntityTypeBuilder<Artwork> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.ImageUrl)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.CreationYear)
            .IsRequired();

        builder.Property(x => x.WidthCm)
            .HasPrecision(8, 2)
            .IsRequired();

        builder.Property(x => x.HeightCm)
            .HasPrecision(8, 2)
            .IsRequired();

        builder.HasOne(x => x.Exhibition)
            .WithMany(x => x.Artworks)
            .HasForeignKey(x => x.ExhibitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}