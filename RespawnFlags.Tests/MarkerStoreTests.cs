using RespawnFlags.Services;
using Xunit;

namespace RespawnFlags.Tests
{
    public class MarkerStoreTests
    {
        [Fact]
        public void Add_FirstMarker_Succeeds()
        {
            var store = new MarkerStore();
            var result = store.TryAdd(100, 50, 200);

            Assert.Equal(MarkerStore.AddResult.Added, result);
            Assert.Equal(1, store.Count);
            Assert.Equal("Marker (100, 200)", store.Markers[0].Name);
        }

        [Fact]
        public void Add_MultipleMarkers_InsertedAtFront()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);
            store.TryAdd(300, 50, 400);

            Assert.Equal(2, store.Count);
            Assert.Equal("Marker (300, 400)", store.Markers[0].Name); // newest first
            Assert.Equal("Marker (100, 200)", store.Markers[1].Name);
        }

        [Fact]
        public void Add_DuplicatePosition_Rejected()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);
            var result = store.TryAdd(102, 50, 201); // within 5 units

            Assert.Equal(MarkerStore.AddResult.Duplicate, result);
            Assert.Equal(1, store.Count);
        }

        [Fact]
        public void Add_NearButNotDuplicate_Accepted()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);
            var result = store.TryAdd(110, 50, 200); // 10 units away

            Assert.Equal(MarkerStore.AddResult.Added, result);
            Assert.Equal(2, store.Count);
        }

        [Fact]
        public void Remove_ValidIndex_RemovesMarker()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);
            store.TryAdd(300, 50, 400);

            store.Remove(0);

            Assert.Equal(1, store.Count);
            Assert.Equal("Marker (100, 200)", store.Markers[0].Name);
        }

        [Fact]
        public void Remove_InvalidIndex_DoesNothing()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);

            store.Remove(-1);
            store.Remove(5);

            Assert.Equal(1, store.Count);
        }

        [Fact]
        public void Rename_ValidIndex_UpdatesName()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);

            store.Rename(0, "My Favorite Spot");

            Assert.Equal("My Favorite Spot", store.Markers[0].Name);
        }

        [Fact]
        public void Rename_TrimsWhitespace()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);

            store.Rename(0, "  Padded Name  ");

            Assert.Equal("Padded Name", store.Markers[0].Name);
        }

        [Fact]
        public void Rename_EmptyString_DoesNothing()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);
            string original = store.Markers[0].Name;

            store.Rename(0, "");
            Assert.Equal(original, store.Markers[0].Name);

            store.Rename(0, "   ");
            Assert.Equal(original, store.Markers[0].Name);
        }

        [Fact]
        public void IsAutoNamed_MatchesAutoFormat()
        {
            Assert.True(MarkerStore.IsAutoNamed("Marker (100, 200)"));
            Assert.True(MarkerStore.IsAutoNamed("Marker (-50, 300)"));
            Assert.True(MarkerStore.IsAutoNamed("Marker (0, 0)"));
        }

        [Fact]
        public void IsAutoNamed_RejectsCustomNames()
        {
            Assert.False(MarkerStore.IsAutoNamed("My Spot"));
            Assert.False(MarkerStore.IsAutoNamed("Race Start"));
            Assert.False(MarkerStore.IsAutoNamed("Marker"));
            Assert.False(MarkerStore.IsAutoNamed("Marker (abc, def)"));
        }

        [Fact]
        public void Eviction_AtCapacity_EvictsOldestUnnamed()
        {
            var store = new MarkerStore();

            // Fill to capacity
            for (int i = 0; i < MarkerStore.MaxMarkers; i++)
                store.TryAdd(i * 100, 0, i * 100);

            Assert.Equal(MarkerStore.MaxMarkers, store.Count);

            // Add one more — should evict the oldest unnamed (last in list)
            var result = store.TryAdd(99999, 0, 99999);

            Assert.Equal(MarkerStore.AddResult.Added, result);
            Assert.Equal(MarkerStore.MaxMarkers, store.Count);
            Assert.Equal("Marker (99999, 99999)", store.Markers[0].Name); // newest at front
        }

        [Fact]
        public void Eviction_ProtectsNamedMarkers()
        {
            var store = new MarkerStore();

            // Fill to capacity
            for (int i = 0; i < MarkerStore.MaxMarkers; i++)
                store.TryAdd(i * 100, 0, i * 100);

            // Rename all except the very last one
            for (int i = 0; i < MarkerStore.MaxMarkers - 1; i++)
                store.Rename(i, $"Named {i}");

            // Add — should evict the one remaining unnamed marker
            var result = store.TryAdd(99999, 0, 99999);

            Assert.Equal(MarkerStore.AddResult.Added, result);
            // All named markers should still exist
            for (int i = 1; i < store.Count; i++)
                Assert.StartsWith("Named", store.Markers[i].Name);
        }

        [Fact]
        public void Eviction_AllNamed_RequiresConfirmation()
        {
            var store = new MarkerStore();

            // Fill to capacity using AddRaw (appends, no index shifting)
            for (int i = 0; i < MarkerStore.MaxMarkers; i++)
                store.AddRaw($"Named {i}", i * 100, 0, i * 100);

            var result = store.TryAdd(99999, 0, 99999);

            Assert.Equal(MarkerStore.AddResult.EvictionRequired, result);
            Assert.True(store.HasPendingEviction);
            Assert.NotNull(store.PendingEvictName);
            Assert.Equal(MarkerStore.MaxMarkers, store.Count); // not yet added
        }

        [Fact]
        public void Eviction_Confirm_AddsMarkerAndRemovesOldest()
        {
            var store = new MarkerStore();

            for (int i = 0; i < MarkerStore.MaxMarkers; i++)
                store.AddRaw($"Named {i}", i * 100, 0, i * 100);

            store.TryAdd(99999, 0, 99999);
            string evictedName = store.PendingEvictName;

            store.ConfirmEviction();

            Assert.False(store.HasPendingEviction);
            Assert.Equal(MarkerStore.MaxMarkers, store.Count);
            Assert.Equal("Marker (99999, 99999)", store.Markers[0].Name);

            // Evicted marker should be gone
            for (int i = 0; i < store.Count; i++)
                Assert.NotEqual(evictedName, store.Markers[i].Name);
        }

        [Fact]
        public void Eviction_Cancel_DiscardsNewMarker()
        {
            var store = new MarkerStore();

            for (int i = 0; i < MarkerStore.MaxMarkers; i++)
                store.AddRaw($"Named {i}", i * 100, 0, i * 100);

            store.TryAdd(99999, 0, 99999);
            store.CancelEviction();

            Assert.False(store.HasPendingEviction);
            Assert.Equal(MarkerStore.MaxMarkers, store.Count);
            // New marker should NOT be present
            Assert.NotEqual("Marker (99999, 99999)", store.Markers[0].Name);
        }

        [Fact]
        public void AddRaw_AppendsAtEnd()
        {
            var store = new MarkerStore();
            store.AddRaw("First", 10, 20, 30);
            store.AddRaw("Second", 40, 50, 60);

            Assert.Equal(2, store.Count);
            Assert.Equal("First", store.Markers[0].Name);
            Assert.Equal("Second", store.Markers[1].Name);
        }

        [Fact]
        public void Clear_RemovesAll()
        {
            var store = new MarkerStore();
            store.TryAdd(100, 50, 200);
            store.TryAdd(300, 50, 400);

            store.Clear();

            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void Add_PreservesPosition()
        {
            var store = new MarkerStore();
            store.TryAdd(123.45f, 67.89f, 234.56f);

            var marker = store.Markers[0];
            Assert.Equal(123.45f, marker.X);
            Assert.Equal(67.89f, marker.Y);
            Assert.Equal(234.56f, marker.Z);
        }

        [Fact]
        public void AddRaw_PreservesCustomName()
        {
            var store = new MarkerStore();
            store.AddRaw("My Special Spot", 10, 20, 30);

            Assert.Equal("My Special Spot", store.Markers[0].Name);
            Assert.False(MarkerStore.IsAutoNamed("My Special Spot"));
        }

        [Fact]
        public void Eviction_SkipsNamedToFindUnnamed()
        {
            var store = new MarkerStore();

            // Add 3 markers: named, unnamed, named
            store.AddRaw("Named First", 100, 0, 100);
            store.AddRaw("Marker (200, 200)", 200, 0, 200);  // auto-named
            store.AddRaw("Named Last", 300, 0, 300);

            // Fill remaining capacity
            for (int i = 3; i < MarkerStore.MaxMarkers; i++)
                store.AddRaw($"Named {i}", i * 100, 0, i * 100);

            // Next add should evict the unnamed one at index 1
            var result = store.TryAdd(99999, 0, 99999);

            Assert.Equal(MarkerStore.AddResult.Added, result);
            // The unnamed marker should be gone
            foreach (var m in store.Markers)
                Assert.NotEqual("Marker (200, 200)", m.Name);
            // Named ones should survive
            Assert.Contains(store.Markers, m => m.Name == "Named First");
            Assert.Contains(store.Markers, m => m.Name == "Named Last");
        }

        [Fact]
        public void Rename_MakesMarkerProtectedFromEviction()
        {
            var store = new MarkerStore();

            // Fill with auto-named markers
            for (int i = 0; i < MarkerStore.MaxMarkers; i++)
                store.TryAdd(i * 100, 0, i * 100);

            // Rename the oldest one (last in list)
            store.Rename(store.Count - 1, "Protected");

            // Add a new marker — should evict the second-oldest unnamed, not "Protected"
            var result = store.TryAdd(99999, 0, 99999);

            Assert.Equal(MarkerStore.AddResult.Added, result);
            Assert.Contains(store.Markers, m => m.Name == "Protected");
        }
    }
}
