
from paddleocr import PaddleOCR
from PIL import Image
import sys
import os

try:
    if len(sys.argv) < 2:
        sys.exit(0)

    image_path = sys.argv[1]
    if not os.path.exists(image_path):
        sys.exit(0)

    ocr = PaddleOCR(use_angle_cls=True, lang="en", show_log=False)
    result = ocr.ocr(image_path, cls=True)

    text = []
    if result:
        for line in result:
            if line is None:
                continue
            for word in line:
                if word and len(word) > 1 and word[1]:
                    text.append(str(word[1][0]))

    print("\n".join(text))
except Exception as ex:
    sys.exit(0)
