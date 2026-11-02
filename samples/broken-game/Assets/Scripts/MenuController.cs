using UnityEngine;

namespace BrokenGame
{
    public class MenuController : MonoBehaviour
    {
        public GameObject menu, options, player;

        public void Play()
        {
            menu.SetActive(false);
            player.SetActive(true);
        }

        public void ShowOptions() => options.SetActive(!options.activeSelf);

        public void Quit() => Application.Quit();
    }
}
