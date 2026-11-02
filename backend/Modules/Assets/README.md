# Assets module

Phase 06 exposes validated GLB/glTF, PNG/JPEG/WebP and OGG/WAV uploads, asset versions, inventory references, permissions, equipment and placed-world instances. PostgreSQL stores metadata and ownership; `AssetStorageService` is the object-storage boundary. Development uses the configured `ObjectStorage:LocalRoot` volume so the same API remains executable without an S3 SDK; production should bind this adapter to the configured MinIO bucket.
