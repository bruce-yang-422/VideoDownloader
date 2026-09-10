import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from yt_dlp import YoutubeDL
from yt_dlp.utils import ExtractorError

ROOT = Path(__file__).resolve().parents[2]
PLUGIN = ROOT / 'Tools/yt-dlp-plugins/videodownloader/yt_dlp_plugins/extractor/threads.py'
spec = importlib.util.spec_from_file_location('vd_threads', PLUGIN)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def video(code):
    return {'code': code, 'user': {'username': 'tester'},
            'caption': {'text': 'Fixture'}, 'original_width': 2160,
            'original_height': 3840, 'has_audio': True,
            'video_versions': [{'url': 'https://cdn.example.test/video.mp4?token=1'}]}


def page(posts, canonical='https://www.threads.com/@tester/post/target'):
    return ('<meta property="og:url" content="' + canonical + '">'
            '<script data-sjs type="application/json">' + json.dumps({'posts': posts}) + '</script>')


class ThreadsTests(unittest.TestCase):
    def setUp(self):
        self.extractor = module.VideoDownloaderThreadsIE(YoutubeDL({'quiet': True}))

    def extract(self, html, url='https://www.threads.com/@tester/post/target/media'):
        with patch.object(self.extractor, '_download_webpage', return_value=html):
            return self.extractor._real_extract(url)

    def test_supported_url_shapes(self):
        for url in ['https://www.threads.com/share/abc/', 'https://threads.net/t/target',
                    'https://www.threads.com/@tester/post/target/media?x=1']:
            self.assertTrue(self.extractor.suitable(url))
        self.assertFalse(self.extractor.suitable('https://threads.com.evil.test/@tester/post/target'))

    def test_select_requested_post_not_recommendation(self):
        result = self.extract(page([video('recommended'), video('target')]))
        self.assertEqual(result['id'], 'target')

    def test_missing_target_never_uses_recommendation(self):
        with self.assertRaises(ExtractorError):
            self.extract(page([video('recommended')]))

    def test_share_uses_canonical_post(self):
        result = self.extract(page([video('recommended'), video('target')]), 'https://www.threads.com/share/abc/')
        self.assertEqual(result['id'], 'target')

    def test_unresolved_share_fails(self):
        with self.assertRaises(ExtractorError):
            self.extract(page([video('recommended')], 'https://www.threads.com/'), 'https://www.threads.com/share/abc/')

    def test_image_post_does_not_select_reply_video(self):
        with self.assertRaises(ExtractorError):
            self.extract(page([{'code': 'target', 'video_versions': []}, video('reply')]))

    def test_carousel_skips_image(self):
        result = self.extract(page([{'code': 'target', 'carousel_media': [{}, video('child')]}]))
        self.assertEqual(result['id'], 'target-2')

    def test_unknown_rendition_does_not_claim_original_resolution(self):
        result = self.extract(page([video('target')]))
        self.assertIsNone(result['formats'][0]['width'])
        self.assertIsNone(result['formats'][0]['height'])

    def test_mpd_keeps_real_video_and_audio_formats(self):
        post = video('target')
        post['video_dash_manifest'] = '''<MPD xmlns="urn:mpeg:dash:schema:mpd:2011" mediaPresentationDuration="PT10S">
        <Period><AdaptationSet mimeType="video/mp4"><Representation id="v" bandwidth="1000000" width="720" height="1280" codecs="avc1.4d401f">
        <BaseURL>https://cdn.example.test/v.mp4</BaseURL></Representation></AdaptationSet>
        <AdaptationSet mimeType="audio/mp4"><Representation id="a" bandwidth="128000" codecs="mp4a.40.2">
        <BaseURL>https://cdn.example.test/a.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>'''
        result = self.extract(page([post]))
        self.assertEqual(result['duration'], 10)
        self.assertTrue(any(f.get('width') == 720 for f in result['formats']))
        self.assertTrue(any(f.get('vcodec') == 'none' for f in result['formats']))


if __name__ == '__main__':
    unittest.main()
