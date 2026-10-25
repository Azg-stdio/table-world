using System.Collections;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// Miguel (2026-10-25): a pawn let go while the hand still moves kept sliding across the table. The hand moves every
    /// frame as a mouse does and lets go mid-motion: a piece is placed (it drops where it is), only a flick throws it, on the felt and on a big
    /// locked board (Master Trainer's pawns stand on its board token).
    /// </summary>
    public class ReleaseSlideTests
    {
        private TableView _table;
        private TableInteraction _interaction;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            _table = Object.FindAnyObjectByType<TableView>();
            yield return new WaitForSeconds(0.2f);
            var json = @"{ ""schemaVersion"": ""0.1"",
                ""assets"": [ { ""id"": ""pawn"", ""type"": ""model"", ""path"": ""builtin:pawn"" } ],
                ""objects"": [
                  { ""id"": ""board"", ""type"": ""Token"", ""size"": 0.6, ""locked"": true, ""transform"": { ""position"": [0.3, 0, 0] } },
                  { ""id"": ""felt_pawn"", ""type"": ""Model"", ""asset"": ""pawn"", ""tint"": [0.8, 0.1, 0.1], ""transform"": { ""position"": [-0.5, 0, 0.2] } },
                  { ""id"": ""board_pawn"", ""type"": ""Model"", ""asset"": ""pawn"", ""tint"": [0.8, 0.1, 0.1], ""transform"": { ""position"": [0.2, 0.02, 0.1] } } ] }";
            Assert.IsTrue(_table.Load(json, null));
            yield return new WaitForSeconds(1f);
        }

        private IEnumerator Throw(string id, Vector3 from, Vector3 to, float speed, bool flick = false)
        {
            var view = _table.ViewOf(id);
            Assert.IsTrue(_interaction.Grab(view, moveContainer: false));
            var distance = Vector3.Distance(from, to);
            for (var t = 0f; t < distance / speed; t += Time.deltaTime)
            {
                _interaction.DragTo(Vector3.Lerp(from, to, t * speed / distance));
                yield return null;
            }
            var released = view.transform.position;
            _interaction.Release();
            yield return new WaitForSeconds(2.5f);
            var slid = Vector2.Distance(new Vector2(released.x, released.z), new Vector2(view.transform.position.x, view.transform.position.z));
            Debug.Log($"[ReleaseSlide] {id} at {speed} m/s slid {slid * 100f:F1} cm, still moving {view.Body.linearVelocity.magnitude:F3} m/s");
            if (flick)
                Assert.Greater(slid, 0.02f, "a flick still throws");
            else
                Assert.Less(slid, 0.01f, $"{id} slid {slid * 100f:F1} cm after being let go");
        }

        [UnityTest]
        public IEnumerator OnTheFelt_APawnStops() => Throw("felt_pawn", new Vector3(-0.5f, 0f, 0.2f), new Vector3(-0.2f, 0f, 0.2f), 0.5f);

        [UnityTest]
        public IEnumerator AFlick_StillThrows() => Throw("felt_pawn", new Vector3(-0.6f, 0f, 0.2f), new Vector3(-0.1f, 0f, 0.2f), 2.5f, flick: true);

        [UnityTest]
        public IEnumerator OnABoard_APawnStops() => Throw("board_pawn", new Vector3(0.2f, 0f, 0.1f), new Vector3(0.4f, 0f, -0.1f), 0.5f);
    }
}
