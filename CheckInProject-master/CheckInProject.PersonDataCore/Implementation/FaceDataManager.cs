using CheckInProject.PersonDataCore.Interfaces;
using CheckInProject.PersonDataCore.Models;
using FaceRecognitionDotNet;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;

namespace CheckInProject.PersonDataCore.Implementation
{
    public class FaceDataManager : IFaceDataManager
    {
        /// <summary>
        /// 人脸识别模型选择
        /// true = 使用CNN模型(Large) - 精度高但慢
        /// false = 使用HOG模型(Small) - 速度快但精度稍低
        /// 修改此处即可切换模型，无需改动其他代码
        /// </summary>
        public static bool UseCnnModel { get; set; } = false; // 默认使用HOG快速模型

        public FaceRecognition FaceRecognitionAPI => Provider.GetRequiredService<FaceRecognition>();

        public CascadeClassifier Cascade => Provider.GetRequiredService<CascadeClassifier>();

        private readonly IServiceProvider Provider;

        public RawPersonDataBase CreateFaceData(Bitmap sourceData, string? sourceName, uint? personID)
        {
            using (var recognitionImage = FaceRecognition.LoadImage(sourceData))
            {
                // 根据配置自动选择模型：CNN(高精度慢速) 或 HOG(低精度快速)
                var model = UseCnnModel ? PredictorModel.Large : PredictorModel.Small;
                var encodings = FaceRecognitionAPI.FaceEncodings(recognitionImage, null, 1, model);
                if (encodings == null || encodings.Count() == 0)
                {
                    throw new InvalidOperationException("未能从图片中提取到人脸特征，请确保图片中包含清晰的人脸。");
                }
                var encoding = encodings.First().GetRawEncoding();
                var personName = sourceName;
                return new RawPersonDataBase { FaceEncoding = encoding, Name = personName, ClassID = personID };
            }
        }

        public RawPersonDataBase CreateFaceDataFast(Bitmap sourceData, string? sourceName, uint? personID)
        {
            // 快速模式始终使用HOG模型
            using (var recognitionImage = FaceRecognition.LoadImage(sourceData))
            {
                var encodings = FaceRecognitionAPI.FaceEncodings(recognitionImage, null, 1, PredictorModel.Small);
                if (encodings == null || encodings.Count() == 0)
                {
                    throw new InvalidOperationException("未能从图片中提取到人脸特征，请确保图片中包含清晰的人脸。");
                }
                var encoding = encodings.First().GetRawEncoding();
                var personName = sourceName;
                return new RawPersonDataBase { FaceEncoding = encoding, Name = personName, ClassID = personID };
            }
        }
        public IList<RawPersonDataBase> CreateFacesData(IList<Bitmap> sourceData)
        {
            var resultFaces = new List<RawPersonDataBase>();
            foreach (var item in sourceData)
            {
                using (var recognitionImage = FaceRecognition.LoadImage(item))
                {
                    var encodings = FaceRecognitionAPI.FaceEncodings(recognitionImage).Select(t => new RawPersonDataBase { FaceEncoding = t.GetRawEncoding() }).ToList();
                    resultFaces.AddRange(encodings);
                }
            }
            return resultFaces;
        }

        public IList<RawPersonDataBase> CompareFace(IList<RawPersonDataBase> faceDataList, RawPersonDataBase targetFaceData)
        {
            var faceEncodingList = faceDataList.Select(t => FaceRecognition.LoadFaceEncoding(t.FaceEncoding)).ToList();
            using (var targetFaceEncoding = FaceRecognition.LoadFaceEncoding(targetFaceData.FaceEncoding))
            {
                var recognizedFaces = FaceRecognition.CompareFaces(faceEncodingList, targetFaceEncoding, 0.4);
                var reconizedNames = new List<RawPersonDataBase>();
                var index = 0;
                foreach (var recognizedFace in recognizedFaces)
                {
                    if (recognizedFace)
                    {
                        var resultName = faceDataList[index];
                        reconizedNames.Add(resultName);
                    }
                    index++;
                }
                foreach (var item in faceEncodingList)
                {
                    item?.Dispose();
                }
                return reconizedNames;
            }
            
        }
        public IList<RawPersonDataBase> CompareFaces(IList<RawPersonDataBase> faceDataList, IList<RawPersonDataBase> targetFaceDataList)
        {
            var faceEncodingList = faceDataList.Select(t => FaceRecognition.LoadFaceEncoding(t.FaceEncoding)).ToList();
            var reconizedNames = new List<RawPersonDataBase>();
            foreach (var targetFaceData in targetFaceDataList)
            {
                using (var targetFaceEncoding = FaceRecognition.LoadFaceEncoding(targetFaceData.FaceEncoding))
                {
                    var recognizedFaces = FaceRecognition.CompareFaces(faceEncodingList, targetFaceEncoding, 0.4);
                    var index = 0;
                    foreach (var recognizedFace in recognizedFaces)
                    {
                        if (recognizedFace)
                        {
                            var resultName = faceDataList[index];
                            reconizedNames.Add(resultName);
                        }
                        index++;
                    }
                }
            }
            foreach (var item in faceEncodingList)
            {
                item?.Dispose();
            } 
            return reconizedNames;
        }
        public FaceCountModels GetFaceCount(Mat sourceImage)
        {
            // 优化参数：更快的检测速度，同时保持合理精度
            Rect[] recognizedFaces = Cascade.DetectMultiScale(
                            image: sourceImage,
                            scaleFactor: 1.2,      // 增大缩放因子，减少检测层数，提速
                            minNeighbors: 3,       // 适当增加邻居数，减少误检
                            flags: HaarDetectionTypes.ScaleImage,
                            minSize: new OpenCvSharp.Size(80, 80),  // 增大最小尺寸，减少小目标检测
                            maxSize: new OpenCvSharp.Size(400, 400) // 限制最大尺寸
                        );
            // ToBitmap()会创建独立的Bitmap副本，Mat可以安全释放
            using (var targetImage = sourceImage.Clone())
            {
                foreach (var recognizedFace in recognizedFaces)
                {
                    targetImage.Rectangle(recognizedFace, Scalar.GreenYellow, 2);
                }
                return new FaceCountModels() { RetangleImage = targetImage.ToBitmap(), Count = recognizedFaces.Length };
            }
        }

        public FaceRetangleModels GetFaceImage(Bitmap sourceBitmap)
        {
            // 不要将Mat放在using块中，因为返回的Bitmap需要它
            var sourceImage = sourceBitmap.ToMat();
            try
            {
                // 优化参数：更快的检测速度
                Rect[] recognizedFaces = Cascade.DetectMultiScale(
                                            image: sourceImage,
                                            scaleFactor: 1.2,
                                            minNeighbors: 3,
                                            flags: HaarDetectionTypes.ScaleImage,
                                            minSize: new OpenCvSharp.Size(80, 80),
                                            maxSize: new OpenCvSharp.Size(400, 400)
                                        );
                var maxiumRect = recognizedFaces.ToList().OrderByDescending(t => t.Height * t.Width).FirstOrDefault();
                
                // 检查是否检测到人脸
                if (maxiumRect == default(Rect) || maxiumRect.Width == 0 || maxiumRect.Height == 0)
                {
                    // 返回原图，不框选
                    var emptyResult = new FaceRetangleModels { FaceImages = new List<Bitmap>(), RetangleImage = sourceImage.ToBitmap() };
                    sourceImage.Dispose(); // 没有检测到人脸时立即释放
                    return emptyResult;
                }
                
                var resultList = new List<Bitmap>();
                using (var resultImage = new Mat(sourceImage, maxiumRect))
                {
                    resultList.Add(resultImage.ToBitmap());
                }
                sourceImage.Rectangle(maxiumRect, Scalar.GreenYellow, 5);
                return new FaceRetangleModels { FaceImages = resultList, RetangleImage = sourceImage.ToBitmap() };
            }
            catch
            {
                sourceImage?.Dispose();
                throw;
            }
        }

        public FaceRetangleModels GetFacesImage(Bitmap sourceBitmap)
        {
            // 不要将Mat放在using块中，因为返回的Bitmap需要它
            var sourceImage = sourceBitmap.ToMat();
            try
            {
                // 优化参数：更快的检测速度
                Rect[] recognizedFaces = Cascade.DetectMultiScale(
                                            image: sourceImage,
                                            scaleFactor: 1.2,
                                            minNeighbors: 3,
                                            flags: HaarDetectionTypes.ScaleImage,
                                            minSize: new OpenCvSharp.Size(80, 80),
                                            maxSize: new OpenCvSharp.Size(400, 400)
                                        );
                var resultList = new List<Bitmap>();
                foreach (var recognizedFace in recognizedFaces)
                {
                    using (var resultImage = new Mat(sourceImage, recognizedFace))
                    {
                        resultList.Add(resultImage.ToBitmap());
                        sourceImage.Rectangle(recognizedFace, Scalar.GreenYellow, 5);
                    }
                }
                return new FaceRetangleModels() { FaceImages = resultList, RetangleImage = sourceImage.ToBitmap() };
            }
            catch
            {
                sourceImage?.Dispose();
                throw;
            }
        }

        public FaceDataManager(IServiceProvider serviceProvider)
        {
            Provider = serviceProvider;
        }
    }
}
