namespace LegendaryTools.Editor
{
    internal sealed class ReferenceTrackerWindowController
    {
        private readonly ReferenceTrackerScopeResolver _scopeResolver;
        private readonly ReferenceTrackerSearchService _searchService;
        private readonly ReferenceTrackerGroupingService _groupingService;
        private readonly ReferenceTrackerSelectionService _selectionService;

        public ReferenceTrackerWindowController(
            ReferenceTrackerScopeResolver scopeResolver,
            ReferenceTrackerSearchService searchService,
            ReferenceTrackerGroupingService groupingService,
            ReferenceTrackerSelectionService selectionService)
        {
            _scopeResolver = scopeResolver;
            _searchService = searchService;
            _groupingService = groupingService;
            _selectionService = selectionService;
        }

        public void NormalizeScopes(ReferenceTrackerWindowState state)
        {
            state.SearchScopes = _scopeResolver.Normalize(state.SearchScopes);
        }

        public void SetGroupMode(ReferenceTrackerWindowState state, ReferenceTrackerGroupMode groupMode)
        {
            state.GroupMode = groupMode;
            RebuildGroups(state);
        }

        public void UseSelection(ReferenceTrackerWindowState state)
        {
            UnityEngine.Object selectedTarget;
            string status;
            if (_selectionService.TryGetSupportedSelection(out selectedTarget, out status))
            {
                state.Target = selectedTarget;
            }

            state.Status = status;
        }

        public void ClearResults(ReferenceTrackerWindowState state)
        {
            state.Results.Clear();
            state.Groups.Clear();
            state.LastSearchDurationMs = 0d;
            state.Status = "Cleared.";
        }

        public void RunSearch(ReferenceTrackerWindowState state)
        {
            ReferenceTrackerSearchResult searchResult = _searchService.Search(state.Target, state.SearchScopes);

            state.Results.Clear();
            state.Results.AddRange(searchResult.Usages);
            state.LastSearchDurationMs = searchResult.DurationMs;
            state.Status = searchResult.Status;
            RebuildGroups(state);
        }

        private void RebuildGroups(ReferenceTrackerWindowState state)
        {
            state.Groups.Clear();
            state.Groups.AddRange(_groupingService.BuildGroups(state.Results, state.GroupMode));
        }
    }
}
