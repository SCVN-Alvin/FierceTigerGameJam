using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Scene-side entry point for map selection. The methods take plain int and string arguments
    /// so a UI Button's OnClick can call them directly without a custom listener.
    /// </summary>
    public sealed class MapSelector : MonoBehaviour
    {
        [SerializeField] private MapSelection mapSelection;

        public MapSelection Selection => mapSelection;

        public void SelectByIndex(int index)
        {
            if (RequireSelection())
            {
                mapSelection.SelectByIndex(index);
            }
        }

        public void SelectById(string id)
        {
            if (RequireSelection())
            {
                mapSelection.SelectById(id);
            }
        }

        public void SelectNext()
        {
            Step(1);
        }

        public void SelectPrevious()
        {
            Step(-1);
        }

        /// <summary>Wraps at both ends so the buttons never dead-end.</summary>
        private void Step(int direction)
        {
            if (!RequireSelection() || mapSelection.Config == null)
            {
                return;
            }

            int count = mapSelection.Config.Count;
            if (count == 0)
            {
                return;
            }

            int current = mapSelection.Config.IndexOf(mapSelection.Selected);
            int next = current < 0 ? 0 : (((current + direction) % count) + count) % count;
            mapSelection.SelectByIndex(next);
        }

        private bool RequireSelection()
        {
            if (mapSelection != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(MapSelector)} needs a {nameof(MapSelection)} asset.", this);
            return false;
        }

        public void TestMap1()
        {
            SelectByIndex(0);
        }

        public void TestMap2()
        {
            SelectByIndex(1);
        }
    }
}
