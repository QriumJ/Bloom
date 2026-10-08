# Builds src/Bloom/Assets/symbols.tsv: the emoji and symbols Bloom's "이모지 · 기호" search offers.
#
#   python tools/make_symbols.py
#
# Which characters are emoji comes from Windows' own ICU (icu.dll, Emoji / Emoji_Presentation properties);
# English names from Python's unicodedata; Korean (and extra English) keywords from the tables below.
# Columns: char, kind (e = emoji, s = symbol), popular rank (0 = not in the starter list), English words,
# Korean words. Re-run after editing the tables; the file is embedded in Bloom.dll.
import ctypes
import os
import unicodedata

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'src', 'Bloom', 'Assets', 'symbols.tsv')

# ── emoji: Korean keywords (and extra English ones after "|") ─────────────────────────────────────
# The order here is also the starter list shown before anything is typed.
EMOJI = """
😀 웃음 웃는 얼굴 미소 기쁨 행복|smile happy
😃 웃음 활짝 기쁨|smile
😄 웃음 눈웃음 기쁨|smile
😁 히죽 웃음 이빨|grin
😆 깔깔 웃음 신남|laugh
😅 땀 웃음 민망 머쓱|sweat
🤣 빵터짐 데굴데굴 폭소 ㅋㅋ|rofl lol
😂 눈물 웃음 폭소 ㅋㅋ ㅋㅋㅋ|lol joy
🙂 미소 살짝 웃음|smile
🙃 거꾸로 장난|upside down
😉 윙크 찡긋|wink
😊 수줍 미소 행복 홍조|blush
😇 천사 착함|angel
🥰 사랑 하트 얼굴 좋아|love
😍 하트 눈 반함 사랑|heart eyes love
🤩 별 눈 반짝 감동 최고|star struck
😘 뽀뽀 키스 하트|kiss
😋 맛있 냠냠 혀|yum
😛 메롱 혀|tongue
😜 장난 윙크 메롱|crazy
🤪 미친 정신없 장난|zany
😝 메롱 눈감|tongue
🤑 돈 부자|money
🤗 포옹 안아줘 허그|hug
🤭 입가림 킥킥 웁스|oops
🤫 쉿 조용 비밀|shush quiet
🤔 생각 고민 흠|thinking hmm
🤐 입다물 지퍼 비밀|zip
🤨 의심 눈썹|suspicious
😐 무표정 멍|neutral
😑 무표정 짜증|expressionless
😶 말없음 침묵|silent
😏 비웃음 썩소 능글|smirk
😒 시큰둥 불만 짜증|unamused
🙄 눈굴림 어이없|eye roll
😬 이빨 어색 민망|grimace awkward
😮‍💨 한숨 휴|exhale sigh
🤥 거짓말 피노키오|lie
😌 안도 편안 휴|relieved
😔 시무룩 슬픔 우울|pensive sad
😪 졸림 콧물|sleepy
🤤 침 군침 맛있겠다|drool
😴 잠 잠자기 쿨쿨 zzz|sleep
😷 마스크 아픔 감기|mask sick
🤒 열 아픔 체온계|fever sick
🤕 다침 붕대|hurt
🤢 메스꺼움 토할것같|nausea
🤮 토 구토|vomit
🤧 재채기 감기 휴지|sneeze
🥵 더위 덥다 땀|hot
🥶 추위 춥다 얼음|cold
🥴 어지러움 술취함 멍|woozy drunk
😵 어지러움 기절|dizzy
🤯 머리폭발 충격 멘붕|mind blown
🤠 카우보이 모자|cowboy
🥳 파티 축하 생일|party celebrate
😎 선글라스 쿨 멋짐|cool sunglasses
🤓 안경 공부 너드|nerd
🧐 모노클 관찰 흠|monocle
😕 혼란 갸웃|confused
😟 걱정 불안|worried
🙁 슬픔 시무룩|frown
😮 놀람 헉|surprised
😯 놀람 와|hushed
😲 깜짝 놀람 충격|astonished
😳 당황 빨개짐 헐|flushed
🥺 부탁 애원 글썽 제발|pleading please
😦 놀람 입벌림|frown
😧 괴로움|anguished
😨 무서움 겁|fearful
😰 식은땀 불안|anxious
😥 실망 안도 땀|sad relieved
😢 눈물 슬픔 울음|cry sad
😭 엉엉 오열 울음 ㅠㅠ|sob cry
😱 비명 공포 충격|scream
😖 괴로움 찡그림|confounded
😣 참는중 힘듦|persevere
😞 실망 낙담|disappointed
😓 식은땀 힘듦|sweat
😩 지침 피곤 힘들어|weary tired
😫 지침 피곤|tired
🥱 하품 졸림 지루|yawn bored
😤 콧김 씩씩 화남 의기양양|huff
😡 화남 분노 빡침|angry rage
😠 화남 짜증|angry
🤬 욕 분노 삐|swearing
😈 악마 장난 웃음|devil
👿 악마 화남|imp
💀 해골 죽음 죽겠다|skull dead
☠️ 해골 독 위험|skull crossbones
💩 똥 응가|poop
🤡 광대 피에로|clown
👻 유령 귀신 할로윈|ghost
👽 외계인|alien
🤖 로봇 봇 ai|robot bot
😺 고양이 웃음|cat
😻 고양이 하트|cat love
🙈 원숭이 안봐 부끄|see no evil
🙉 원숭이 안들려|hear no evil
🙊 원숭이 쉿|speak no evil
❤️ 하트 빨간|=heart red
🧡 주황 하트|orange heart
💛 노란 하트|yellow heart
💚 초록 하트|green heart
💙 파란 하트|blue heart
💜 보라 하트|purple heart
🖤 검은 하트|black heart
🤍 흰 하트|white heart
🤎 갈색 하트|brown heart
🩷 분홍 하트 핑크|pink heart
🩵 하늘색 하트|light blue heart
🩶 회색 하트|grey heart
💔 깨진 하트 실연 상처|broken heart
❤️‍🔥 불타는 하트 열정|heart on fire
❤️‍🩹 회복 하트 붕대|mending heart
💕 하트 두개 사랑|two hearts
💞 회전 하트|revolving hearts
💓 두근 하트|beating heart
💗 커지는 하트|growing heart
💖 반짝 하트|sparkling heart
💘 큐피드 화살 하트|cupid
💝 리본 하트 선물|heart gift
💯 백점 100 완벽|hundred perfect
💢 화남 표시 빡|anger
💥 쾅 폭발 충돌|boom collision
💫 어지러움 별|dizzy
💦 땀방울 물|sweat droplets
💨 휙 바람 빨리|dash
💬 말풍선 대화 채팅|speech chat
💭 생각 풍선|thought
💤 잠 쿨쿨 zzz|zzz sleep
👋 안녕 손인사 바이|wave hello bye
🤚 손등 손|raised back of hand
✋ 손 멈춰 하이파이브|hand stop
🖐️ 손바닥 다섯|hand
🖖 벌칸|vulcan
👌 오케이 좋아 ok|ok
🤌 이탈리아 손|pinched
🤏 조금 살짝|pinch little
✌️ 브이 승리 평화|victory peace v
🤞 행운 손가락 꼬기|fingers crossed luck
🫰 손가락 하트|finger heart
🤟 사랑해|love you
🤘 락 메탈|rock
🤙 전화해 샤카|call me
👈 왼쪽 가리킴|left
👉 오른쪽 가리킴|right
👆 위 가리킴|up
👇 아래 가리킴|down
☝️ 검지 하나 위|index up
🫵 너 가리킴|you
👍 좋아요 최고 따봉 엄지척|thumbs up like ok good
👎 싫어요 별로 엄지 아래|thumbs down dislike bad
✊ 주먹|fist
👊 주먹 펀치|punch
🤛 주먹 왼쪽|fist
🤜 주먹 오른쪽|fist
👏 박수 짝짝 축하|clap applause
🙌 만세 축하 두손|raised hands hooray
🫶 손하트 사랑|heart hands
👐 손 펼침|open hands
🤲 두손 모음|palms
🤝 악수 협력 동의|handshake deal
🙏 기도 부탁 감사 제발|pray please thanks
✍️ 글쓰기 손|writing
💅 매니큐어 네일|nail polish
💪 힘 근육 화이팅 운동|muscle strong
🧠 뇌 생각 똑똑|brain
👀 눈 보기 지켜봄|eyes look
👁️ 눈 하나|eye
👄 입술|mouth
🫦 입술 깨물|biting lip
👶 아기|baby
🧒 아이 어린이|child
👦 소년 남자아이|boy
👧 소녀 여자아이|girl
🧑 사람|person
👨 남자|man
👩 여자|woman
🧓 어르신|older person
👴 할아버지|old man
👵 할머니|old woman
🙇 사과 죄송 꾸벅|bow sorry
🤦 이마짚기 아이고 한심|facepalm
🤷 몰라 으쓱|shrug
🙆 오케이 동그라미|ok gesture
🙅 안돼 엑스|no gesture
💁 안내 정보|tipping hand
🙋 손들기 저요|raising hand
🧑‍💻 개발자 프로그래머 코딩|technologist developer coder
🧑‍🎨 화가 예술가|artist
🧑‍🍳 요리사 셰프|cook chef
🧑‍🎓 학생 졸업|student
🧑‍🏫 선생님 교사|teacher
🧑‍⚕️ 의사 간호사|health worker
👮 경찰|police
🕵️ 탐정|detective
👑 왕관 왕 최고|crown king
🎅 산타 크리스마스|santa
🧚 요정|fairy
🧜 인어|merperson
🧙 마법사|mage wizard
🧛 뱀파이어|vampire
🧟 좀비|zombie
💃 춤 댄스|dance
🕺 춤 댄스 남자|dance
🏃 달리기 뛰기|run
🚶 걷기 산책|walk
🧘 요가 명상|yoga meditation
🛌 잠자리 침대|bed
🐶 강아지 개 멍멍|dog puppy
🐱 고양이 냥이 야옹|cat kitty
🐭 쥐|mouse
🐹 햄스터|hamster
🐰 토끼|rabbit bunny
🦊 여우|fox
🐻 곰|bear
🐼 판다|panda
🐨 코알라|koala
🐯 호랑이|tiger
🦁 사자|lion
🐮 소|cow
🐷 돼지|pig
🐸 개구리|frog
🐵 원숭이|monkey
🐔 닭|chicken
🐧 펭귄|penguin
🐦 새|bird
🐤 병아리|chick
🦆 오리|duck
🦅 독수리|eagle
🦉 부엉이 올빼미|owl
🦇 박쥐|bat
🐺 늑대|wolf
🐴 말|horse
🦄 유니콘|unicorn
🐝 벌 꿀벌|bee
🐛 애벌레|bug
🦋 나비|butterfly
🐌 달팽이 느림|snail slow
🐞 무당벌레|ladybug
🐢 거북이 느림|turtle slow
🐍 뱀|snake
🐙 문어|octopus
🦑 오징어|squid
🦀 게|crab
🐠 열대어 물고기|fish
🐟 물고기 생선|fish
🐬 돌고래|dolphin
🐳 고래|whale
🦈 상어|shark
🐊 악어|crocodile
🐘 코끼리|elephant
🦒 기린|giraffe
🐕 개|dog
🐈 고양이|cat
🐾 발자국|paw prints
🌸 벚꽃 꽃 봄|cherry blossom flower spring
💐 꽃다발|bouquet
🌹 장미|rose
🌷 튤립|tulip
🌻 해바라기|sunflower
🌼 꽃|blossom
🌱 새싹|seedling sprout
🌿 풀 허브|herb
🍀 네잎클로버 행운|four leaf clover luck
🍁 단풍 가을|maple autumn
🍂 낙엽 가을|fallen leaf autumn
🌲 나무 침엽수|tree
🌳 나무|tree
🌴 야자수|palm tree
🌵 선인장|cactus
🍄 버섯|mushroom
🌍 지구 세계|earth globe world
🌙 달 밤 초승달|moon night
🌕 보름달|full moon
⭐ 별|star
🌟 반짝별|glowing star
✨ 반짝 반짝이 빛|sparkles shine
⚡ 번개 전기 빠름|lightning zap
🔥 불 핫 대박 인기|fire hot lit
🌈 무지개|rainbow
☀️ 해 맑음 태양|sun sunny
🌤️ 구름 조금|sun cloud
⛅ 구름 해|partly cloudy
☁️ 구름 흐림|cloud
🌧️ 비|rain
⛈️ 천둥 번개 비|storm
❄️ 눈송이 겨울|snowflake winter
☃️ 눈사람|snowman
⛄ 눈사람|snowman
🌊 파도 바다|wave ocean
💧 물방울|droplet water
☔ 우산 비|umbrella rain
🍎 사과|apple
🍊 귤 오렌지|orange tangerine
🍋 레몬|lemon
🍌 바나나|banana
🍉 수박|watermelon
🍇 포도|grapes
🍓 딸기|strawberry
🍑 복숭아|peach
🍒 체리|cherries
🥭 망고|mango
🍍 파인애플|pineapple
🥝 키위|kiwi
🍅 토마토|tomato
🥑 아보카도|avocado
🥕 당근|carrot
🌽 옥수수|corn
🌶️ 고추 매움|hot pepper spicy
🥦 브로콜리|broccoli
🧄 마늘|garlic
🧅 양파|onion
🥔 감자|potato
🍞 빵|bread
🥐 크루아상|croissant
🧀 치즈|cheese
🥚 달걀 계란|egg
🍳 계란후라이 요리|cooking egg
🥓 베이컨|bacon
🍗 치킨 닭다리|chicken leg
🍖 고기|meat
🍔 햄버거|hamburger burger
🍟 감자튀김|fries
🍕 피자|pizza
🌭 핫도그|hot dog
🌮 타코|taco
🍜 라면 국수|ramen noodles
🍝 스파게티 파스타|spaghetti pasta
🍣 초밥 스시|sushi
🍱 도시락|bento
🍙 주먹밥 삼각김밥|rice ball
🍚 밥|rice
🍛 카레|curry
🍲 찌개 전골|stew pot
🥘 냄비 요리|paella
🥗 샐러드|salad
🍿 팝콘|popcorn
🍩 도넛|donut
🍪 쿠키|cookie
🎂 케이크 생일|birthday cake
🍰 조각케이크|cake
🧁 컵케이크|cupcake
🍫 초콜릿|chocolate
🍬 사탕|candy
🍭 막대사탕|lollipop
🍦 아이스크림 소프트|ice cream
🍨 아이스크림|ice cream
☕ 커피 카페|coffee
🍵 차 녹차|tea
🧋 버블티 밀크티|bubble tea boba
🥤 음료 컵|cup drink
🍺 맥주|beer
🍻 건배 맥주|cheers beers
🥂 건배 샴페인|cheers toast
🍷 와인|wine
🍸 칵테일|cocktail
🍶 사케 술|sake
🥢 젓가락|chopsticks
🍴 포크 나이프 식사|fork knife
🎉 축하 파티 폭죽|party tada congrats
🎊 축하 색종이|confetti
🎈 풍선|balloon
🎁 선물|gift present
🎀 리본|ribbon
🎄 크리스마스 트리|christmas tree
🎃 호박 할로윈|jack o lantern halloween
🎆 불꽃놀이|fireworks
🎇 불꽃|sparkler
🧨 폭죽|firecracker
🏆 트로피 우승 1등|trophy
🥇 금메달 1등|gold medal first
🥈 은메달 2등|silver medal
🥉 동메달 3등|bronze medal
🏅 메달|medal
⚽ 축구|soccer football
⚾ 야구|baseball
🏀 농구|basketball
🏐 배구|volleyball
🎾 테니스|tennis
🏸 배드민턴|badminton
🏓 탁구|ping pong
⛳ 골프|golf
🎮 게임 게임패드|video game controller
🕹️ 조이스틱 게임|joystick
🎲 주사위|dice
🧩 퍼즐|puzzle
🎯 다트 목표 명중|target bullseye
🎨 그림 미술 팔레트|art palette
🎬 영화 촬영|movie clapper
🎤 마이크 노래 노래방|microphone sing
🎧 헤드폰 음악|headphones music
🎵 음표 음악|music note
🎶 음악 노래|music notes
🎸 기타|guitar
🎹 피아노 건반|piano keyboard
🥁 드럼|drum
📷 카메라 사진|camera photo
📸 사진찍기 플래시|camera flash
📹 캠코더 영상|video camera
🎥 영화 카메라|movie camera
📺 텔레비전 티비|tv television
📻 라디오|radio
📱 휴대폰 스마트폰|mobile phone
💻 노트북 컴퓨터|laptop computer
🖥️ 데스크톱 모니터|desktop computer
⌨️ 키보드|keyboard
🖱️ 마우스|mouse
🖨️ 프린터|printer
💾 저장 플로피|save floppy
💿 시디|cd
🔋 배터리|battery
🔌 플러그 전원|plug
💡 아이디어 전구|idea bulb
🔦 손전등|flashlight
🕯️ 양초|candle
📚 책 공부|books
📖 책 읽기|book read
📝 메모 노트 작성|memo note
✏️ 연필|pencil
🖊️ 펜|pen
🖌️ 붓|paintbrush
📌 압정 고정 핀|pin pushpin
📍 위치 핀|location pin
📎 클립 첨부|paperclip attachment
✂️ 가위 자르기|scissors cut
📁 폴더|folder
📂 열린 폴더|open folder
📅 달력 날짜|calendar date
📆 일정 달력|calendar
🗓️ 일정|calendar
📊 막대 그래프 차트|bar chart
📈 상승 그래프 증가|chart increasing up
📉 하락 그래프 감소|chart decreasing down
📋 클립보드 목록|clipboard
📦 상자 택배 패키지|package box
📫 우편함|mailbox
✉️ 편지 메일|envelope mail
📧 이메일|email
📨 받은 메일|incoming envelope
🔔 알림 종|bell notification
🔕 알림 끔 무음|no bell mute
📢 확성기 공지|loudspeaker announce
📣 메가폰 응원|megaphone
🔍 검색 돋보기|search magnifier
🔎 검색 돋보기|search
🔒 잠금 자물쇠|lock locked
🔓 잠금해제|unlock
🔑 열쇠 키|key
🗝️ 오래된 열쇠|old key
🔨 망치|hammer
🔧 렌치 스패너 수리|wrench fix
🛠️ 도구 공구|tools
⚙️ 톱니 설정|gear settings
🧲 자석|magnet
🧪 실험 시험관|test tube
💊 약 알약|pill
💉 주사|syringe
🩹 반창고|bandage
🧹 빗자루 청소|broom clean
🧺 바구니 빨래|basket
🧻 휴지|toilet paper
🛒 장바구니 쇼핑|cart shopping
💰 돈 주머니|money bag
💵 달러 지폐|dollar
💴 엔 지폐|yen
💳 카드 신용카드 결제|credit card
🪙 동전|coin
💎 다이아몬드 보석|gem diamond
⌚ 시계 손목시계|watch
⏰ 알람 시계 기상|alarm clock
⏱️ 스톱워치|stopwatch
⏳ 모래시계 기다림|hourglass waiting
⌛ 모래시계 끝|hourglass done
🏠 집 홈|home house
🏡 정원 집|house garden
🏢 빌딩 회사|office building
🏫 학교|school
🏥 병원|hospital
🏪 편의점|convenience store
⛪ 교회|church
🗼 탑 도쿄타워|tower
🗽 자유의 여신상|statue of liberty
🏰 성|castle
⛺ 텐트 캠핑|tent camping
🌃 야경 밤|night city
🚗 자동차 차|car
🚕 택시|taxi
🚌 버스|bus
🚇 지하철|metro subway
🚆 기차|train
🚄 고속열차 ktx|bullet train
🚲 자전거|bicycle bike
🛵 스쿠터 배달|scooter
🏍️ 오토바이|motorcycle
✈️ 비행기 여행|airplane flight travel
🛫 이륙 출발|departure
🛬 착륙 도착|arrival
🚀 로켓 출시 발사 빠름|rocket launch
🛸 유에프오|ufo
⛵ 요트|sailboat
🚢 배 선박|ship
⚓ 닻|anchor
🚦 신호등|traffic light
🚧 공사중|construction
⛽ 주유소|fuel
🗺️ 지도|map
🧭 나침반|compass
⛰️ 산|mountain
🏖️ 해변 바다 휴가|beach
🏝️ 섬|island
✅ 체크 완료 확인 됨|check done yes
☑️ 체크박스|check box
✔️ 체크|check mark
❌ 엑스 아니오 틀림 취소|x cross no wrong
❎ 엑스 버튼|cross mark button
➕ 더하기 플러스|plus
➖ 빼기 마이너스|minus
➗ 나누기|divide
✖️ 곱하기|multiply
❓ 물음표 질문|question
❔ 물음표|question
❗ 느낌표 중요|exclamation
❕ 느낌표|exclamation
‼️ 느낌표 두개|double exclamation
⁉️ 느낌표 물음표|interrobang
⚠️ 경고 주의|warning
🚫 금지 안됨|prohibited no
⛔ 진입금지 금지|no entry
🔴 빨간 원|red circle
🟠 주황 원|orange circle
🟡 노란 원|yellow circle
🟢 초록 원|green circle
🔵 파란 원|blue circle
🟣 보라 원|purple circle
⚫ 검은 원|black circle
⚪ 흰 원|white circle
🟥 빨간 네모|red square
🟩 초록 네모|green square
🟦 파란 네모|blue square
⬛ 검은 네모|black square
⬜ 흰 네모|white square
🔶 주황 마름모|orange diamond
🔷 파란 마름모|blue diamond
🔺 빨간 삼각형 위|red triangle up
🔻 빨간 삼각형 아래|red triangle down
♻️ 재활용|recycle
🆗 오케이|ok button
🆕 새로운 new|new
🆓 무료 free|free
🆙 업|up button
🆒 쿨|cool button
🔝 탑 위로|top
🔙 뒤로 back|back
🔜 곧 soon|soon
💲 달러 기호|dollar sign
©️ 저작권|copyright
®️ 등록상표|registered
™️ 상표|trademark
🔞 19금 성인|eighteen
📵 휴대폰 금지|no mobile
🔇 음소거 소리끔|mute
🔈 소리 작게|speaker low
🔉 소리|speaker medium
🔊 소리 크게|speaker loud
🔁 반복|repeat
🔂 한곡 반복|repeat one
▶️ 재생|play
⏸️ 일시정지|pause
⏹️ 정지|stop
⏺️ 녹화|record
⏭️ 다음|next track
⏮️ 이전|previous track
⏩ 빨리감기|fast forward
⏪ 되감기|rewind
🔀 셔플 섞기|shuffle
⬆️ 위 화살표|up arrow
⬇️ 아래 화살표|down arrow
⬅️ 왼쪽 화살표|left arrow
➡️ 오른쪽 화살표|right arrow
↩️ 되돌아가기|return
🔄 새로고침 회전|refresh arrows
🏁 체크기 결승|chequered flag finish
🚩 깃발 빨간|red flag
🏳️ 흰 깃발 항복|white flag
🏴 검은 깃발|black flag
🇰🇷 한국 태극기 대한민국|korea flag south korea
🇺🇸 미국 성조기|usa united states flag
🇯🇵 일본 국기|japan flag
🇨🇳 중국 국기|china flag
🇬🇧 영국 국기|uk united kingdom flag
🇫🇷 프랑스 국기|france flag
🇩🇪 독일 국기|germany flag
🇮🇹 이탈리아 국기|italy flag
🇪🇸 스페인 국기|spain flag
🇨🇦 캐나다 국기|canada flag
🇦🇺 호주 국기|australia flag
🇹🇼 대만 국기|taiwan flag
🇻🇳 베트남 국기|vietnam flag
🇹🇭 태국 국기|thailand flag
🏳️‍🌈 무지개 깃발 프라이드|rainbow flag pride
"""

# ── symbols: Korean keywords and English ones after "|" ─────────────────────────────────────────
SYMBOLS = """
· 가운뎃점 중점 점|middle dot interpunct
• 글머리 점 불릿|bullet
… 말줄임표 점점점|ellipsis
※ 참고 당구장 주의|reference mark note
— 긴 줄표 대시|em dash
– 짧은 줄표|en dash
→ 오른쪽 화살표 화살|right arrow
← 왼쪽 화살표|left arrow
↑ 위 화살표|up arrow
↓ 아래 화살표|down arrow
↔ 양쪽 화살표|left right arrow
↕ 위아래 화살표|up down arrow
⇒ 이중 화살표 따라서|rightwards double arrow implies
⇐ 이중 왼쪽 화살표|leftwards double arrow
⇔ 동치 양쪽 이중|left right double arrow iff
↗ 오른쪽 위 화살표|north east arrow
↘ 오른쪽 아래 화살표|south east arrow
↖ 왼쪽 위 화살표|north west arrow
↙ 왼쪽 아래 화살표|south west arrow
↩ 되돌림 화살표|return arrow
↪ 넘어감 화살표|hook arrow
⟶ 긴 화살표|long right arrow
➜ 굵은 화살표|heavy arrow
▶ 재생 삼각형 오른쪽|play triangle right
◀ 삼각형 왼쪽|triangle left
▲ 삼각형 위 증가|triangle up
▼ 삼각형 아래 감소|triangle down
△ 빈 삼각형|white triangle up
▽ 빈 삼각형 아래|white triangle down
● 검은 원 동그라미|black circle
○ 흰 원 동그라미|white circle
◎ 이중 원|bullseye
◉ 원 점|fisheye
■ 검은 네모 사각형|black square
□ 흰 네모 사각형|white square
◆ 검은 마름모|black diamond
◇ 흰 마름모|white diamond
★ 검은 별|black star
☆ 흰 별 빈별|white star
♥ 하트 검은|heart suit
♡ 빈 하트|white heart suit
♠ 스페이드|spade
♣ 클로버|club
♦ 다이아|diamond suit
♪ 음표|eighth note music
♫ 음표 두개|beamed notes music
✓ 체크 표시|check mark tick
✔ 굵은 체크|heavy check
✗ 엑스 표시|ballot x
✘ 굵은 엑스|heavy ballot x
☐ 빈 체크박스|ballot box
☑ 체크된 박스|ballot box check
☒ 엑스 박스|ballot box x
℃ 섭씨 도 온도|degree celsius
℉ 화씨|degree fahrenheit
° 도 각도 온도|degree
′ 분 프라임|prime minute
″ 초 더블 프라임|double prime second
‰ 퍼밀 천분율|per mille
± 플러스마이너스 오차|plus minus
× 곱하기 곱셈|multiplication times
÷ 나누기 나눗셈|division
≠ 같지않음 다름|not equal
≈ 거의같음 약|almost equal approximately
≒ 약 근사|approximately equal
≡ 합동 항등|identical
≤ 작거나같음 이하|less than or equal
≥ 크거나같음 이상|greater than or equal
∞ 무한대 무한|infinity
√ 루트 제곱근|square root
∑ 시그마 합|sum sigma
∏ 곱 파이|product
∫ 적분|integral
∂ 편미분|partial
∆ 델타 변화|increment delta
∇ 나블라|nabla
∈ 원소 속함|element of
∉ 속하지않음|not element
⊂ 부분집합|subset
⊃ 포함|superset
∪ 합집합|union
∩ 교집합|intersection
∅ 공집합|empty set
∀ 모든|for all
∃ 존재|exists
¬ 부정 not|not sign
∧ 그리고 and|logical and
∨ 또는 or|logical or
∴ 그러므로 따라서|therefore
∵ 왜냐하면|because
∠ 각|angle
⊥ 수직|perpendicular
∥ 평행|parallel
π 파이 원주율|pi
α 알파|alpha
β 베타|beta
γ 감마|gamma
δ 델타|delta
ε 엡실론|epsilon
θ 세타|theta
λ 람다|lambda
μ 뮤 마이크로|mu micro
σ 시그마|sigma
φ 파이|phi
ω 오메가|omega
Ω 오메가 옴|omega ohm
½ 이분의일 반|one half
⅓ 삼분의일|one third
¼ 사분의일|one quarter
¾ 사분의삼|three quarters
¹ 위첨자 1|superscript one
² 제곱 위첨자 2|superscript two squared
³ 세제곱 위첨자 3|superscript three cubed
ⁿ 위첨자 n|superscript n
₁ 아래첨자 1|subscript one
₂ 아래첨자 2|subscript two
€ 유로|euro
£ 파운드|pound
¥ 엔 위안|yen yuan
₩ 원 원화|won
₿ 비트코인|bitcoin
¢ 센트|cent
§ 섹션 조항|section
¶ 단락 문단|pilcrow paragraph
† 단검 사망|dagger
‡ 이중 단검|double dagger
© 저작권 카피라이트|copyright
® 등록상표|registered
™ 상표 트레이드마크|trademark
℡ 전화|telephone sign
№ 번호 넘버|numero number
㈜ 주식회사|corporation
㉿ 대한민국 우편|korean postal
㎡ 제곱미터|square meter
㎥ 세제곱미터|cubic meter
㎏ 킬로그램|kilogram
㎎ 밀리그램|milligram
㎞ 킬로미터|kilometer
㎝ 센티미터|centimeter
㎜ 밀리미터|millimeter
㎖ 밀리리터|milliliter
ℓ 리터|liter
「 낫표 여는|left corner bracket
」 낫표 닫는|right corner bracket
『 겹낫표 여는|left white corner bracket
』 겹낫표 닫는|right white corner bracket
【 굵은 괄호 여는|left black lenticular bracket
】 굵은 괄호 닫는|right black lenticular bracket
〈 홑화살괄호 여는|left angle bracket
〉 홑화살괄호 닫는|right angle bracket
《 겹화살괄호 여는|left double angle bracket
》 겹화살괄호 닫는|right double angle bracket
“ 큰따옴표 여는|left double quotation
” 큰따옴표 닫는|right double quotation
‘ 작은따옴표 여는|left single quotation
’ 작은따옴표 닫는 아포스트로피|right single quotation apostrophe
« 기유메 여는|left guillemet
» 기유메 닫는|right guillemet
¿ 거꾸로 물음표|inverted question
¡ 거꾸로 느낌표|inverted exclamation
⌘ 커맨드 맥|command
⌥ 옵션 맥|option
⇧ 시프트|shift
⌃ 컨트롤|control
⏎ 엔터 리턴|return enter
⌫ 백스페이스 지우기|backspace erase
⎋ 이스케이프 esc|escape
⇥ 탭|tab
␣ 스페이스 공백|space
♂ 남성 수컷|male
♀ 여성 암컷|female
☀ 해 태양|sun
☁ 구름|cloud
☂ 우산|umbrella
☎ 전화기|telephone
☺ 웃는 얼굴|smiling face
☻ 검은 웃는 얼굴|black smiling face
✿ 꽃|flower
❀ 꽃 흰|white florette
❄ 눈송이|snowflake
☯ 음양|yin yang
☮ 평화|peace
✝ 십자가|cross
☪ 초승달 별|star and crescent
⚑ 깃발|flag
✉ 편지|envelope
✂ 가위|scissors
✎ 연필|pencil
♨ 온천|hot springs
⚠ 경고|warning
☢ 방사능|radioactive
☣ 생물학적 위험|biohazard
⚡ 번개|high voltage
❥ 하트 장식|rotated heart bullet
❣ 하트 느낌표|heart exclamation
❝ 큰 따옴표 장식|quote ornament
❞ 큰 따옴표 장식 닫는|quote ornament close
┌ 박스 왼쪽위 선|box drawings down right
┐ 박스 오른쪽위|box drawings down left
└ 박스 왼쪽아래|box drawings up right
┘ 박스 오른쪽아래|box drawings up left
├ 박스 왼쪽 가지 트리|box drawings vertical right tree
─ 가로선|box drawings horizontal line
│ 세로선 파이프|box drawings vertical line
━ 굵은 가로선|heavy horizontal
┃ 굵은 세로선|heavy vertical
═ 이중 가로선|double horizontal
║ 이중 세로선|double vertical
░ 옅은 음영|light shade
▒ 중간 음영|medium shade
▓ 진한 음영|dark shade
█ 꽉찬 블록|full block
▌ 왼쪽 반 블록|left half block
▐ 오른쪽 반 블록|right half block
"""


def icu():
    lib = ctypes.CDLL(r'C:\Windows\System32\icu.dll')
    f = lib.u_hasBinaryProperty
    f.restype = ctypes.c_int8
    f.argtypes = [ctypes.c_int32, ctypes.c_int]
    return f


def name_of(s):
    words = []
    for ch in s:
        if ch in ('\ufe0f', '\u200d'):
            continue
        n = unicodedata.name(ch, '')
        if 0x1F1E6 <= ord(ch) <= 0x1F1FF:
            n = ''  # regional indicator letters: the flag's English word comes from the table
        if n:
            words.append(n.lower())
    return ' '.join(words)


def parse(table):
    rows = []
    for line in table.strip().splitlines():
        line = line.strip()
        if not line:
            continue
        ch, rest = line.split(' ', 1)
        ko, _, en = rest.partition('|')
        rows.append((ch, ko.strip(), en.strip()))
    return rows


def main():
    has = icu()
    out = []
    seen = set()

    def add(ch, kind, rank, en_extra, ko):
        if ch in seen:
            return
        seen.add(ch)
        # the table's own words first (they say what it is: "heart" for ❤️), then the Unicode name; each word once.
        # "=words" keeps only the table's words: ❤️'s Unicode name is "heavy black heart", and few words rank it first
        # for 하트 (fewer words = more about that word)
        if en_extra.startswith('='):
            en = ' '.join(dict.fromkeys(en_extra[1:].split()))
        else:
            en = ' '.join(dict.fromkeys(' '.join([en_extra, name_of(ch)]).split()))
        out.append('\t'.join([ch, kind, str(rank), en, ko]))

    for rank, (ch, ko, en) in enumerate(parse(EMOJI), 1):
        add(ch, 'e', rank, en, ko)
    for rank, (ch, ko, en) in enumerate(parse(SYMBOLS), 1):
        add(ch, 's', rank, en, ko)
    # every other single-character emoji Windows knows, with its English name
    for cp in range(0x20, 0x110000):
        if not has(cp, 57):  # Emoji
            continue
        if has(cp, 59) or has(cp, 62) or has(cp, 61) and not has(cp, 58):  # modifiers, regional indicators, components
            continue
        if cp < 0x80:
            continue  # # * 0-9
        ch = chr(cp) if has(cp, 58) else chr(cp) + '\ufe0f'
        if not name_of(ch):
            continue
        add(ch, 'e', 0, '', '')
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# made by tools/make_symbols.py — char, kind, rank, english, korean\n')
        f.write('\n'.join(out) + '\n')
    print(f'{len(out)} symbols → {OUT}')


if __name__ == '__main__':
    main()
