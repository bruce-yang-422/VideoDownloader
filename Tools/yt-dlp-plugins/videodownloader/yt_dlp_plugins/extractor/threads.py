"""Public Threads post support bundled with VideoDownloader.

Uses yt-dlp's plugin API and the public link-preview page. No account cookies
are read. Always match the requested post code before selecting media.
"""

import json
import re
import xml.etree.ElementTree as ET
from urllib.parse import urlparse

from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.utils import ExtractorError, float_or_none, int_or_none, parse_duration, url_or_none


class VideoDownloaderThreadsIE(InfoExtractor):
    IE_NAME = 'videodownloader:threads'
    _VALID_URL = (r'https?://(?:www\.)?threads\.(?:com|net)/'
                  r'(?:(?:@[^/?#]+/post|t)/(?P<id>[A-Za-z0-9_-]+)'
                  r'|share/(?P<share>[A-Za-z0-9_-]+))(?:[/?#]|$)')
    _PREVIEW_UA = 'Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)'

    @staticmethod
    def _find_post(html, code):
        for tag, body in re.findall(r'<script\b([^>]*)>(.*?)</script\s*>', html, re.S | re.I):
            if not re.search(r'type\s*=\s*[\"\']application/json[\"\']', tag, re.I):
                continue
            try:
                pending = [json.loads(body)]
            except ValueError:
                continue
            while pending:
                node = pending.pop()
                if isinstance(node, dict):
                    if node.get('code') == code and any(k in node for k in ('video_versions', 'video_dash_manifest', 'carousel_media')):
                        return node
                    pending.extend(reversed(list(node.values())))
                elif isinstance(node, list):
                    pending.extend(reversed(node))
        return None

    def _media_formats(self, media, code):
        formats, duration = [], float_or_none(media.get('video_duration'))
        manifest = media.get('video_dash_manifest')
        if isinstance(manifest, str) and manifest:
            try:
                document = ET.fromstring(manifest)
                formats = self._parse_mpd_formats(document, mpd_id='threads')
                duration = duration or parse_duration(document.get('mediaPresentationDuration'))
            except (ET.ParseError, ExtractorError, ValueError):
                self.report_warning('無法解析 Threads 的部分畫質資料，嘗試直接影片連結')
        # MPD supplies actual rendition dimensions and audio/video codecs.
        # Do not label dimensionless progressive copies with the original post size.
        if not any(f.get('vcodec') != 'none' for f in formats):
            seen = set()
            for rendition in media.get('video_versions') or []:
                media_url = url_or_none(rendition.get('url'))
                if not media_url or urlparse(media_url).scheme not in ('https', 'http'):
                    continue
                key = urlparse(media_url)._replace(query='', fragment='').geturl()
                if key in seen:
                    continue
                seen.add(key)
                formats.append({
                    'format_id': f'threads-http-{len(seen)}', 'url': media_url, 'ext': 'mp4',
                    'width': int_or_none(rendition.get('width')),
                    'height': int_or_none(rendition.get('height')),
                    'vcodec': 'unknown',
                    'acodec': 'none' if media.get('has_audio') is False else 'unknown',
                })
        for fmt in formats:
            fmt.setdefault('http_headers', {})['Referer'] = 'https://www.threads.com/'
        return formats, duration

    def _real_extract(self, url):
        match = self._match_valid_url(url)
        requested = match.group('id')
        html = self._download_webpage(url, requested or match.group('share'),
                                      headers={'User-Agent': self._PREVIEW_UA})
        canonical = self._og_search_property('url', html, default=None)
        resolved = re.match(self._VALID_URL, canonical or '')
        code = requested or (resolved.group('id') if resolved else None)
        if not code:
            raise ExtractorError('無法辨識 Threads 分享連結，請貼上含 /post/ 的完整貼文網址。', expected=True)
        post = self._find_post(html, code)
        if post is None:
            raise ExtractorError('Threads 未提供這則貼文的公開影片資料；可能需要登入、已刪除或網站版面已變更。', expected=True)
        carousel = post.get('carousel_media')
        for index, media in enumerate(carousel or [post], 1):
            if not isinstance(media, dict):
                continue
            formats, duration = self._media_formats(media, code)
            if not formats:
                continue
            user = post.get('user') or {}
            caption = (post.get('caption') or {}).get('text') or ''
            title = caption.splitlines()[0][:100] if caption else f'Threads 影片 {code}'
            if carousel:
                title += f'（貼文第 {index} 項）'
            candidates = (media.get('image_versions2') or {}).get('candidates') or []
            thumbnail = next((c.get('url') for c in candidates if url_or_none(c.get('url'))), None)
            return {
                'id': f'{code}-{index}' if carousel else code,
                'title': title, 'description': caption,
                'uploader': user.get('full_name') or user.get('username'),
                'uploader_id': user.get('username'),
                'duration': duration, 'thumbnail': thumbnail,
                'webpage_url': canonical or url, 'formats': formats,
            }
        raise ExtractorError('這則 Threads 貼文沒有可下載影片，可能只有文字或圖片。', expected=True)
